from fastapi import FastAPI
from pydantic import BaseModel
from typing import List
import joblib
from sklearn.ensemble import IsolationForest
from sklearn.preprocessing import StandardScaler
import numpy as np
import os

app = FastAPI()
MODEL_PATH = "models/latest.pkl"
os.makedirs("models", exist_ok=True)

FEATURES = ["Requests", "FailCount", "ErrorRatio", "AvgInterRequestMs", "DistinctUsers"]

class Metric(BaseModel):
    MinuteUtc: str
    IpAddress: str
    Requests: int
    SuccessCount: int
    FailCount: int
    ErrorRatio: float
    AvgInterRequestMs: float
    DistinctUsers: int

class TrainBody(BaseModel):
    contamination: float = 0.02
    metrics: List[Metric]

class ScoreBody(BaseModel):
    metrics: List[Metric]

def fit_transform_X(metrics):
    X = []
    for m in metrics:
        X.append([m.Requests, m.FailCount, m.ErrorRatio, m.AvgInterRequestMs, m.DistinctUsers])
    X = np.array(X, dtype=float)
    return X

@app.post("/train")
def train(body: TrainBody):
    X = fit_transform_X(body.metrics)
    scaler = StandardScaler()
    Xs = scaler.fit_transform(X)

    model = IsolationForest(
        n_estimators=200,
        contamination=body.contamination,
        random_state=42,
        n_jobs=-1
    )
    model.fit(Xs)

    joblib.dump({"scaler": scaler, "model": model, "version": "v1"}, MODEL_PATH)
    return {"ok": True, "model_version": "v1", "n": len(X)}

@app.post("/score")
def score(body: ScoreBody):
    obj = joblib.load(MODEL_PATH)
    scaler = obj["scaler"]
    model = obj["model"]
    X = fit_transform_X(body.metrics)
    Xs = scaler.transform(X)
    # predict: 1 normal, -1 anomalía
    y = model.predict(Xs).tolist()
    # decision_function: mayor => más normal; menor => más anómalo
    df = model.decision_function(Xs).tolist()
    out = []
    for m, label, s in zip(body.metrics, y, df):
        out.append({
            "MinuteUtc": m.MinuteUtc,
            "IpAddress": m.IpAddress,
            "IsAnomaly": (label == -1),
            "Score": float(-s)  # invertimos para que mayor => más sospechoso
        })
    return {"results": out}

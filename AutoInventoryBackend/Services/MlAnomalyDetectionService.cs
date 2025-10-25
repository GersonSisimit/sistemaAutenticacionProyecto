using Frontend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Frontend.Controllers
{
    public class DashboardController : Controller
    {
        private readonly HttpClient _http;

        public DashboardController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _http = httpClientFactory.CreateClient();
            _http.BaseAddress = new Uri(config["BackendApi:BaseUrl"]);
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var token = context.HttpContext.Session.GetString("JwtToken");
            if (string.IsNullOrEmpty(token))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            base.OnActionExecuting(context);
        }

        public async Task<IActionResult> Index()
        {
            var summaryResp = await _http.GetAsync("api/dashboard/summary");
            DashboardSummary? summary = null;
            if (summaryResp.IsSuccessStatusCode)
            {
                var json = await summaryResp.Content.ReadAsStringAsync();
                summary = JsonSerializer.Deserialize<DashboardSummary>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            // Top sospechosas
            var suspicious = await _http.GetAsync("api/anomaly/top-suspicious?minutes=60&take=10");
            if (suspicious.IsSuccessStatusCode && summary != null)
            {
                var json = await suspicious.Content.ReadAsStringAsync();
                summary.TopSuspicious = JsonSerializer.Deserialize<List<SuspiciousIp>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            // Serie general
            var series = await _http.GetAsync("api/anomaly/series?minutes=60");
            if (series.IsSuccessStatusCode && summary != null)
            {
                var json = await series.Content.ReadAsStringAsync();
                summary.Series = JsonSerializer.Deserialize<List<SeriesPoint>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            // NEW: bloqueos por minuto
            var blockedSeriesResp = await _http.GetAsync("api/anomaly/blocked-series?minutes=60");
            if (blockedSeriesResp.IsSuccessStatusCode && summary != null)
            {
                var json = await blockedSeriesResp.Content.ReadAsStringAsync();
                summary.BlockedSeries = JsonSerializer.Deserialize<List<BlockedPointVM>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            // NEW: top IPs bloqueadas
            var blockedTopResp = await _http.GetAsync("api/anomaly/blocked-top?minutes=60&take=10");
            if (blockedTopResp.IsSuccessStatusCode && summary != null)
            {
                var json = await blockedTopResp.Content.ReadAsStringAsync();
                summary.BlockedTop = JsonSerializer.Deserialize<List<BlockedIpVM>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            return View(summary);
        }

        [HttpGet]
        public async Task<IActionResult> Ip(string ip, int minutes = 60)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                TempData["Error"] = "Debes indicar una IP.";
                return RedirectToAction(nameof(Index));
            }

            var resp = await _http.GetAsync($"api/anomaly/ip/{Uri.EscapeDataString(ip)}?minutes={minutes}");
            if (!resp.IsSuccessStatusCode)
            {
                TempData["Error"] = $"No se pudo obtener el detalle para la IP {ip}.";
                return RedirectToAction(nameof(Index));
            }

            var json = await resp.Content.ReadAsStringAsync();
            var detail = JsonSerializer.Deserialize<IpDetail>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new IpDetail { Ip = ip };

            return View(detail); // Renderiza Views/Dashboard/Ip.cshtml
        }
    }
}

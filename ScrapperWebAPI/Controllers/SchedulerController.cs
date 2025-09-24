using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using ScrapperWebAPI.Services;

namespace ScrapperWebAPI.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class SchedulerController : ControllerBase
    {
        private readonly WeeklyCategorySchedulerService _schedulerService;
        private readonly ILogger<SchedulerController> _logger;

        public SchedulerController(IServiceProvider serviceProvider, ILogger<SchedulerController> logger)
        {
            _logger = logger;

            var hostedServices = serviceProvider.GetServices<IHostedService>();
            _schedulerService = hostedServices.OfType<WeeklyCategorySchedulerService>().FirstOrDefault();
        }

        [HttpGet("status")]
        public IActionResult GetSchedulerStatus()
        {
            try
            {
                if (_schedulerService == null)
                {
                    return NotFound(new { message = "Scheduler service tapılmadı" });
                }

                var status = _schedulerService.GetServiceStatus();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Status yoxlanılarkən xəta");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("next-run")]
        public IActionResult GetNextExecutionTime()
        {
            try
            {
                if (_schedulerService == null)
                {
                    return NotFound(new { message = "Scheduler service tapılmadı" });
                }

                var nextExecution = _schedulerService.GetNextExecutionTime();
                var timeUntilNext = nextExecution - DateTime.Now;

                return Ok(new
                {
                    nextExecutionTime = nextExecution.ToString("dd.MM.yyyy HH:mm:ss"),
                    dayOfWeek = nextExecution.ToString("dddd"),
                    timeUntilNext = new
                    {
                        days = timeUntilNext.Days,
                        hours = timeUntilNext.Hours,
                        minutes = timeUntilNext.Minutes,
                        totalHours = (int)timeUntilNext.TotalHours,
                        formatted = $"{timeUntilNext.Days} gün, {timeUntilNext.Hours} saat, {timeUntilNext.Minutes} dəqiqə"
                    },
                    isToday = nextExecution.Date == DateTime.Today,
                    currentTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Növbəti icra vaxtı yoxlanılarkən xəta");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartManually()
        {
            try
            {
                if (_schedulerService == null)
                {
                    return NotFound(new { message = "Scheduler service tapılmadı" });
                }

                _logger.LogInformation("Manual scheduler başladılır");

                await _schedulerService.TriggerManualExecution();

                return Ok(new
                {
                    message = "Scheduler manual olaraq başladıldı",
                    startedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
                    note = "Proses uzun çəkə bilər. Status endpoint-i ilə izləyin",
                    statusEndpoint = "/api/v1/scheduler/status"
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Scheduler artıq işləyir: {Error}", ex.Message);
                return BadRequest(new
                {
                    error = "Scheduler artıq işləyir",
                    message = ex.Message,
                    suggestion = "Status endpoint-i ilə vəziyyəti yoxlayın"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual execution başladılarkən xəta");
                return StatusCode(500, new
                {
                    error = "Manual execution zamanı xəta baş verdi",
                    details = ex.Message
                });
            }
        }

        [HttpGet("info")]
        public IActionResult GetSchedulerInfo()
        {
            var isServiceAvailable = _schedulerService != null;

            return Ok(new
            {
                serviceName = "Weekly Category Scheduler",
                version = "1.0",
                description = "Hər həftənin çərşənbə günü saat 15:00-da bütün mağazaları avtomatik işləyir",
                isServiceAvailable = isServiceAvailable,

                schedule = new
                {
                    day = "Çərşənbə",
                    time = "15:00",
                    timezone = "Lokal vaxt",
                    frequency = "Həftəlik"
                },

                workflow = new
                {
                    description = "Hər mağaza tam bitdikdən sonra növbətiyə keçir",
                    steps = new[]
                    {
                        "1. GoSport - Kategoriyalar yüklənir və external API-yə göndərilir",
                        "2. GoSport - Bütün kategoriyaların məhsulları işlənir və göndərilir",
                        "3. 30 saniyə fasilə",
                        "4. Zara - Kategoriyalar yüklənir və external API-yə göndərilir",
                        "5. Zara - Bütün kategoriyaların məhsulları işlənir və göndərilir"
                    }
                },

                supportedStores = new[] { "gosport", "zara" },

                endpoints = new
                {
                    status = new
                    {
                        method = "GET",
                        path = "/api/v1/scheduler/status",
                        description = "Scheduler-in cari vəziyyətini qaytarır"
                    },
                    nextRun = new
                    {
                        method = "GET",
                        path = "/api/v1/scheduler/next-run",
                        description = "Növbəti avtomatik işləmə vaxtını göstərir"
                    },
                    startManually = new
                    {
                        method = "POST",
                        path = "/api/v1/scheduler/start",
                        description = "Scheduler-i manual olaraq işə salır"
                    },
                    info = new
                    {
                        method = "GET",
                        path = "/api/v1/scheduler/info",
                        description = "Scheduler haqqında ətraflı məlumat"
                    },
                    health = new
                    {
                        method = "GET",
                        path = "/api/v1/scheduler/health",
                        description = "Service-in sağlamlığını yoxlayır"
                    }
                },

                externalApis = new
                {
                    categories = "http://69.62.114.202:5009/api/v1/category-stock/add-category",
                    products = "http://69.62.114.202:5009/api/v1/products-stock/add-products"
                },

                features = new[]
                {
                    "Avtomatik həftəlik icra",
                    "Manual trigger imkanı",
                    "Real-time status izləmə",
                    "Detallı log sistemi",
                    "Xəta handling və retry mexanizmi",
                    "Sıralı mağaza işləmə",
                    "Batch processing",
                    "Timeout və retry konfiqurasiyası"
                }
            });
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            try
            {
                var isServiceRegistered = _schedulerService != null;

                if (!isServiceRegistered)
                {
                    return StatusCode(503, new
                    {
                        status = "Unhealthy",
                        message = "Scheduler service qeydiyyatdan keçməyib",
                        serviceRegistered = false,
                        timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
                    });
                }

                var nextRun = _schedulerService.GetNextExecutionTime();
                var timeUntilNext = nextRun - DateTime.Now;

                return Ok(new
                {
                    status = "Healthy",
                    message = "Scheduler service normal işləyir",
                    serviceRegistered = true,
                    currentTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
                    nextScheduledRun = nextRun.ToString("dd.MM.yyyy HH:mm:ss"),
                    timeUntilNextRun = timeUntilNext.TotalHours > 0 ?
                        $"{timeUntilNext.Days} gün, {timeUntilNext.Hours} saat" :
                        "Tezliklə işləyəcək",
                    uptime = "Service aktiv",
                    supportedStores = new[] { "gosport", "zara" }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check zamanı xəta");
                return StatusCode(500, new
                {
                    status = "Unhealthy",
                    message = "Health check zamanı xəta baş verdi",
                    error = ex.Message,
                    timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
                });
            }
        }

        [HttpGet("logs")]
        public IActionResult GetRecentLogs()
        {
            return Ok(new
            {
                message = "Log məlumatları console-da görünür",
                suggestion = "Console logs və ya log fayllarını yoxlayın",
                logSources = new[]
                {
                    "Console output",
                    "Visual Studio Output window",
                    "Application logs folder"
                },
                sampleLogFormat = new
                {
                    info = "info: Mağaza 1/2: GOSPORT başlayır...",
                    warning = "warn: External API xətası: BadRequest",
                    error = "fail: GOSPORT: NIKE xətası: Timeout"
                }
            });
        }

        [HttpPost("stop")]
        public IActionResult StopScheduler()
        {
            return Ok(new
            {
                message = "Scheduler dayandırma funksiyası mövcud deyil",
                note = "Service yalnız application restart ilə dayandırıla bilər",
                alternatives = new[]
                {
                    "Application-ı restart edin",
                    "IHostedService lifecycle istifadə edin"
                }
            });
        }
    }
}
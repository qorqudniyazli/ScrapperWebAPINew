using Newtonsoft.Json;
using ScrapperWebAPI.Helpers;
using ScrapperWebAPI.Helpers.Product;
using System.Text;

namespace ScrapperWebAPI.Services;

public class WeeklyCategorySchedulerService : BackgroundService
{
    private readonly ILogger<WeeklyCategorySchedulerService> _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly TimeSpan _scheduledTime = new TimeSpan(19, 00, 0);
    private readonly DayOfWeek _scheduledDay = DayOfWeek.Wednesday;
    private readonly List<string> _supportedStores = new List<string> { "gosport", "zara" };

    public WeeklyCategorySchedulerService(
        ILogger<WeeklyCategorySchedulerService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Weekly Category Scheduler Service başladılır...");
        _logger.LogInformation("Planlaşdırılan vaxt: Hər çərşənbə günü saat 15:00");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextRunTime = CalculateNextRunTime();
                var timeUntilNextRun = nextRunTime - DateTime.Now;

                _logger.LogInformation($"Növbəti icra vaxtı: {nextRunTime:dd.MM.yyyy HH:mm:ss}");
                _logger.LogInformation($"Gözləmə müddəti: {timeUntilNextRun.Days} gün, {timeUntilNextRun.Hours} saat, {timeUntilNextRun.Minutes} dəqiqə");

                if (timeUntilNextRun > TimeSpan.Zero)
                {
                    await Task.Delay(timeUntilNextRun, stoppingToken);
                }

                if (stoppingToken.IsCancellationRequested)
                    break;

                await ExecuteScheduledTask();

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weekly Scheduler xətası");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        _logger.LogInformation("Weekly Category Scheduler Service dayandırıldı");
    }

    private DateTime CalculateNextRunTime()
    {
        var now = DateTime.Now;
        var daysUntilWednesday = ((int)_scheduledDay - (int)now.DayOfWeek + 7) % 7;
        var nextWednesday = now.Date.AddDays(daysUntilWednesday).Add(_scheduledTime);

        if (nextWednesday <= now)
        {
            nextWednesday = nextWednesday.AddDays(7);
        }

        return nextWednesday;
    }

    private async Task ExecuteScheduledTask()
    {
        _logger.LogInformation("Planlaşdırılan Categories task-ı başlayır...");

        using var scope = _serviceProvider.CreateScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        // Sira ile her magazani tam bitir
        for (int i = 0; i < _supportedStores.Count; i++)
        {
            var store = _supportedStores[i];
            try
            {
                _logger.LogInformation($"Mağaza {i + 1}/{_supportedStores.Count}: {store.ToUpper()} başlayır...");

                // Categories yukle ve gonder
                var categories = await ProcessStoreCategories(store, httpClientFactory);

                // Bu magazanin BUTUN productlarini tam bitir
                await ProcessAllProductsForStore(store, categories);

                _logger.LogInformation($"Mağaza {i + 1}/{_supportedStores.Count}: {store.ToUpper()} TAMAMLANDİ");

                // Magazalar arası fasile
                if (i < _supportedStores.Count - 1)
                {
                    _logger.LogInformation("Növbəti mağazaya keçmədən əvvəl 30 saniyə fasilə...");
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{store.ToUpper()} mağazası üçün xəta: {ex.Message}");
                continue;
            }
        }

        _logger.LogInformation("Bütün mağazalar üçün planlaşdırılan task tamamlandı!");
    }

    private async Task<List<object>> ProcessStoreCategories(string store, IHttpClientFactory httpClientFactory)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        List<object> dataToSend = new List<object>();

        if (store.ToLower() == "gosport")
        {
            var data = await GetGoSportBrands.GetAll();
            foreach (var item in data)
            {
                dataToSend.Add(new
                {
                    name = item.Name,
                    type = item.Type,
                    img = item.Img,
                    store = store
                });
            }
        }
        else if (store.ToLower() == "zara")
        {
            var data = await GetZaraCategories.GetAll();
            foreach (var item in data)
            {
                dataToSend.Add(new
                {
                    name = item.Name,
                    type = item.Type,
                    img = item.Img,
                    store = store
                });
            }
        }

        if (dataToSend.Count > 0)
        {
            _logger.LogInformation($"{store.ToUpper()}: {dataToSend.Count} kateqoriya tapıldı");

            await SendToExternalApi(dataToSend, httpClient);

            _logger.LogInformation($"{store.ToUpper()}: Categories external API-yə göndərildi");
        }
        else
        {
            _logger.LogWarning($"{store.ToUpper()}: Heç bir kateqoriya tapılmadı");
        }

        return dataToSend;
    }

    // Bu magazanin BUTUN categorylarinin productlarini tam bitir
    private async Task ProcessAllProductsForStore(string store, List<object> categories)
    {
        var categoryNames = new List<string>();

        foreach (var category in categories)
        {
            var nameProperty = category.GetType().GetProperty("name");
            if (nameProperty != null)
            {
                var nameValue = nameProperty.GetValue(category)?.ToString();
                if (!string.IsNullOrEmpty(nameValue))
                {
                    categoryNames.Add(nameValue);
                }
            }
        }

        if (categoryNames.Count == 0)
        {
            _logger.LogWarning($"{store.ToUpper()}: Məhsul işləmək üçün kateqoriya yoxdur");
            return;
        }

        _logger.LogInformation($"{store.ToUpper()}: {categoryNames.Count} kateqoriya üçün məhsullar işlənəcək");

        // Her kategori ucun mehsullari işle
        for (int i = 0; i < categoryNames.Count; i++)
        {
            var categoryName = categoryNames[i];
            try
            {
                _logger.LogInformation($"{store.ToUpper()}: [{i + 1}/{categoryNames.Count}] {categoryName} məhsulları işlənir...");

                await ProcessCategoryProducts(store, categoryName);

                _logger.LogInformation($"{store.ToUpper()}: [{i + 1}/{categoryNames.Count}] {categoryName} tamamlandı");

                // Kategoriyalar arası qısa fasile
                if (i < categoryNames.Count - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{store.ToUpper()}: {categoryName} xətası: {ex.Message}");
                continue;
            }
        }

        _logger.LogInformation($"{store.ToUpper()}: Bütün məhsullar tamamlandı");
    }

    private async Task ProcessCategoryProducts(string store, string categoryName)
    {
        try
        {
            var products = new List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto>();

            if (store.ToLower() == "gosport")
            {
                products = await GetGoSportProducts.GetByProductByBrand(categoryName);
            }
            else if (store.ToLower() == "zara")
            {
                products = await GetZaraProduct.GetByCategoryName(categoryName);
            }

            if (products == null || products.Count == 0)
            {
                _logger.LogWarning($"{store.ToUpper()}: {categoryName} üçün məhsul tapılmadı");
                return;
            }

            _logger.LogInformation($"{store.ToUpper()}: {categoryName} üçün {products.Count} məhsul tapıldı");

            // Products external API-ye gonder
            await SendProductsToExternalApi(products, store, categoryName);

            _logger.LogInformation($"{store.ToUpper()}: {categoryName} məhsulları external API-yə göndərildi");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{store.ToUpper()}: {categoryName} məhsul işləmə xətası");
            throw;
        }
    }

    private async Task SendProductsToExternalApi(List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto> products, string store, string categoryName)
    {
        using var scope = _serviceProvider.CreateScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(15); // Timeout artırıldı

        var productsForAPI = new List<object>();

        foreach (var product in products)
        {
            var sizes = new List<object>();
            if (product.Sizes != null)
            {
                foreach (var size in product.Sizes)
                {
                    sizes.Add(new { sizeName = size.SizeName, onStock = size.OnStock });
                }
            }

            var colors = new List<object>();
            if (product.Colors != null)
            {
                foreach (var color in product.Colors)
                {
                    colors.Add(new { name = color.Name, hex = color.Hex });
                }
            }

            var productData = new
            {
                name = product.Name ?? "",
                brand = product.Brand ?? "",
                price = product.Price,
                productUrl = product.ProductUrl ,
                discountedPrice = product.DiscountedPrice,
                description = !string.IsNullOrEmpty(product.Description) && product.Description.Length > 150
                    ? product.Description.Substring(0, 150) + "..."
                    : product.Description ?? "",
                images = product.ImageUrl ?? new List<string>(),
                sizes = sizes,
                colors = colors,
                store = store,
                category = categoryName,
                processedAt = DateTime.Now.ToString("HH:mm:ss")
            };

            productsForAPI.Add(productData);
        }

        // Batch size kiçildildi
        const int batchSize = 20;
        var batches = ChunkList(productsForAPI, batchSize);

        _logger.LogInformation($"{store.ToUpper()}: {categoryName} - {productsForAPI.Count} məhsul {batches.Count} hissədə göndəriləcək");

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            const int maxRetries = 3;

            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(batch);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await httpClient.PostAsync(
                        "http://69.62.114.202:5009/api/v1/products-stock/add-products",
                        content);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"{store.ToUpper()}: {categoryName} - Hissə {i + 1}/{batches.Count} uğurla göndərildi");
                        break; // Uğurlu oldu, retry döngüsündən çıx
                    }
                    else
                    {
                        _logger.LogWarning($"{store.ToUpper()}: {categoryName} - Hissə {i + 1}/{batches.Count} xətası: {response.StatusCode}");

                        if (retry == maxRetries - 1)
                        {
                            _logger.LogError($"{store.ToUpper()}: {categoryName} - Hissə {i + 1} son cəhddən sonra uğursuz");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"{store.ToUpper()}: {categoryName} - Hissə {i + 1}/{batches.Count} göndərmə xətası (Cəhd {retry + 1})");

                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5 * (retry + 1))); // Progressive delay
                    }
                }
            }

            // Batch-lər arası fasilə
            if (i < batches.Count - 1)
            {
                await Task.Delay(2000); // 2 saniyə
            }
        }
    }

    private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.Skip(i).Take(chunkSize).ToList());
        }
        return chunks;
    }

    private async Task SendToExternalApi(List<object> data, HttpClient httpClient)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(
                "http://69.62.114.202:5009/api/v1/category-stock/add-category",
                content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("External API-yə uğurla göndərildi");
            }
            else
            {
                _logger.LogWarning($"External API xətası: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Error response: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External API-yə göndərmə xətası");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Weekly Category Scheduler Service dayandırılır...");
        await base.StopAsync(cancellationToken);
    }

    public async Task TriggerManualExecution()
    {
        _logger.LogInformation("Manual trigger aktivləşdirildi");
        await ExecuteScheduledTask();
    }

    public DateTime GetNextExecutionTime()
    {
        return CalculateNextRunTime();
    }

    public object GetServiceStatus()
    {
        var nextRun = CalculateNextRunTime();
        var timeUntilNext = nextRun - DateTime.Now;

        return new
        {
            serviceName = "Weekly Category Scheduler",
            isRunning = true,
            scheduledDay = "Çərşənbə",
            scheduledTime = "15:00",
            nextExecutionTime = nextRun.ToString("dd.MM.yyyy HH:mm:ss"),
            timeUntilNextExecution = $"{timeUntilNext.Days} gün, {timeUntilNext.Hours} saat, {timeUntilNext.Minutes} dəqiqə",
            supportedStores = _supportedStores
        };
    }
}
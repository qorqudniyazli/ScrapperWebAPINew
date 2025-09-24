using ScrapperWebAPI.Helpers.Product;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ScrapperWebAPI.Services
{
    public static class ProductSyncManager
    {
        private static readonly ConcurrentQueue<CategorySyncItem> _syncQueue = new();
        private static readonly ILogger _logger;
        private static bool _isRunning = false;
        private static bool _isProcessing = false;

        // JSON nəticələrini saxlamaq üçün
        private static readonly List<object> _allResults = new();
        private static string _lastProcessedCategory = "";
        private static DateTime _lastProcessedTime = DateTime.MinValue;
        private static int _totalProductsProcessed = 0;

        static ProductSyncManager()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger("ProductSyncManager");

            _logger.LogInformation("ProductSyncManager hazır");
        }

        // Helper method for chunking
        private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
        {
            var chunks = new List<List<T>>();
            for (int i = 0; i < source.Count; i += chunkSize)
            {
                chunks.Add(source.Skip(i).Take(chunkSize).ToList());
            }
            return chunks;
        }

        public static void StartAutoSync(List<string> categoryNames, string store)
        {
            try
            {
                _logger.LogInformation("Auto sync başladılır: {Store} üçün {Count} kategori", store, categoryNames.Count);

                // Queue-ni təmizlə və yeni kategoriyaları əlavə et
                while (_syncQueue.TryDequeue(out _)) { }

                foreach (var categoryName in categoryNames)
                {
                    _syncQueue.Enqueue(new CategorySyncItem
                    {
                        CategoryName = categoryName,
                        Store = store
                    });
                }

                _isRunning = true;

                // Dərhal başla (background task)
                Task.Run(ProcessAllCategories);

                _logger.LogInformation("Avtomatik kategori keçidi aktivləşdi");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto sync başladılarkən xəta");
            }
        }

        private static async Task ProcessAllCategories()
        {
            while (_isRunning && !_syncQueue.IsEmpty)
            {
                if (_isProcessing)
                {
                    await Task.Delay(1000);
                    continue;
                }

                _isProcessing = true;

                try
                {
                    if (_syncQueue.TryDequeue(out var syncItem))
                    {
                        _logger.LogInformation("Kategori işlənir: {Store} - {Category}",
                            syncItem.Store, syncItem.CategoryName);

                        var products = await CallProductsAPI(syncItem.Store, syncItem.CategoryName);

                        // Nəticələri saxla və API-yə göndər
                        await SaveResults(products, syncItem);

                        _logger.LogInformation("{Store} - {Category} tamamlandı: {Count} məhsul",
                            syncItem.Store, syncItem.CategoryName, products?.Count ?? 0);

                        if (products?.Count > 0)
                        {
                            _logger.LogInformation("Nümunələr: {Product1}, {Product2}",
                                products[0].Name,
                                products.Count > 1 ? products[1].Name : "N/A");
                        }

                        // Cycle üçün yenidən queue-ya əlavə et
                        _syncQueue.Enqueue(syncItem);

                        _logger.LogInformation("Növbəti kategoriyaya keçir... ({Remaining} qalır)",
                            _syncQueue.Count - 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Category işlənərkən xəta");
                }
                finally
                {
                    _isProcessing = false;

                    // Kategoriyalar arası qısa fasilə
                    await Task.Delay(2000);
                }
            }
        }

        private static async Task SaveResults(List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto> products, CategorySyncItem syncItem)
        {
            try
            {
                _lastProcessedCategory = $"{syncItem.Store} - {syncItem.CategoryName}";
                _lastProcessedTime = DateTime.Now;

                if (products?.Count > 0)
                {
                    _totalProductsProcessed += products.Count;

                    // JSON formatına çevir (external API üçün)
                    var productsForAPI = new List<object>();

                    // Hər məhsulu təktək əlavə et
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
                            discountedPrice = product.DiscountedPrice,
                            description = !string.IsNullOrEmpty(product.Description) && product.Description.Length > 150
                                ? product.Description.Substring(0, 150) + "..."
                                : product.Description ?? "",
                            images = product.ImageUrl ?? new List<string>(),
                            sizes = sizes,
                            colors = colors,
                            store = syncItem.Store,
                            category = syncItem.CategoryName,
                            processedAt = DateTime.Now.ToString("HH:mm:ss")
                        };

                        _allResults.Add(productData);
                        productsForAPI.Add(productData);
                    }

                    _logger.LogInformation("{Count} məhsul saxlandı. Cəmi: {Total}",
                        products.Count, _allResults.Count);

                    // External API-yə göndər
                    await SendToExternalAPI(productsForAPI, syncItem);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nəticələr saxlanılarkən xəta");
            }
        }

        private static async Task SendToExternalAPI(List<object> products, CategorySyncItem syncItem)
        {
            try
            {
                // Böyük məhsul siyahısını kiçik hissələrə böl
                const int batchSize = 50; // Hər dəfə 50 məhsul göndər
                var batches = ChunkList(products, batchSize);

                _logger.LogInformation("External API-yə göndərilir: {Count} məhsul ({BatchCount} hissədə) - {Store} {Category}",
                    products.Count, batches.Count, syncItem.Store, syncItem.CategoryName);

                for (int i = 0; i < batches.Count; i++)
                {
                    var batch = batches[i];

                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 dəqiqə timeout

                    var json = JsonSerializer.Serialize(batch);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                    _logger.LogInformation("Hissə {Current}/{Total} göndərilir: {BatchSize} məhsul",
                        i + 1, batches.Count, batch.Count);

                    var response = await httpClient.PostAsync(
                        "http://69.62.114.202:5009/api/v1/products-stock/add-products",
                        content);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Hissə {Current}/{Total} uğurla göndərildi", i + 1, batches.Count);
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Hissə {Current}/{Total} xətası: {StatusCode}",
                            i + 1, batches.Count, response.StatusCode);
                        _logger.LogWarning("Error Response: {ErrorContent}", errorContent);
                    }

                    // Hissələr arası qısa fasilə
                    if (i < batches.Count - 1)
                    {
                        await Task.Delay(1000); // 1 saniyə gözlə
                    }
                }

                _logger.LogInformation("External API göndərmə tamamlandı: {Store} - {Category}",
                    syncItem.Store, syncItem.CategoryName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "External API-yə göndərmə xətası: {Store} - {Category}",
                    syncItem.Store, syncItem.CategoryName);
            }
        }

        private static async Task<List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto>> CallProductsAPI(string store, string category)
        {
            try
            {
                if (store.ToLower() == "gosport")
                {
                    return await GetGoSportProducts.GetByProductByBrand(category);
                }
                else if (store.ToLower() == "zara")
                {
                    return await GetZaraProduct.GetByCategoryName(category);
                }

                return new List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Products API xətası: {Store} - {Category}", store, category);
                return new List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto>();
            }
        }

        public static object GetCurrentStatus()
        {
            return new
            {
                isActive = _isRunning,
                isProcessing = _isProcessing,
                queueCount = _syncQueue.Count,
                lastProcessedCategory = _lastProcessedCategory,
                lastProcessedTime = _lastProcessedTime.ToString("dd.MM.yyyy HH:mm:ss"),
                totalProductsCollected = _allResults.Count,
                totalProductsProcessed = _totalProductsProcessed
            };
        }

        public static object GetLastResults()
        {
            var last5Products = _allResults.TakeLast(5).ToList();

            return new
            {
                totalProductsCollected = _allResults.Count,
                lastProcessedCategory = _lastProcessedCategory,
                lastProcessedTime = _lastProcessedTime.ToString("dd.MM.yyyy HH:mm:ss"),
                showing = "Son 5 məhsul",
                products = last5Products
            };
        }

        public class CategorySyncItem
        {
            public string CategoryName { get; set; }
            public string Store { get; set; }
        }
    }
}
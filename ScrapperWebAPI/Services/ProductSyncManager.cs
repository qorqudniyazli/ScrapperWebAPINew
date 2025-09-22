using ScrapperWebAPI.Helpers.Product;
using System.Collections.Concurrent;

namespace ScrapperWebAPI.Services;

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

        _logger.LogInformation("⏳ ProductSyncManager hazır");
    }

    public static async void StartAutoSync(List<string> categoryNames, string store)
    {
        try
        {
            _logger.LogInformation("🚀 Auto sync başladılır: {Store} üçün {Count} kategori", store, categoryNames.Count);

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
            _ = Task.Run(ProcessAllCategories);

            _logger.LogInformation("✅ Avtomatik kategori keçidi aktivləşdi");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Auto sync başladılarkən xəta");
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
                    _logger.LogInformation("🔄 Kategori işlənir: {Store} - {Category}",
                        syncItem.Store, syncItem.CategoryName);

                    var products = await CallProductsAPI(syncItem.Store, syncItem.CategoryName);

                    // Nəticələri saxla
                    SaveResults(products, syncItem);

                    _logger.LogInformation("✅ {Store} - {Category} tamamlandı: {Count} məhsul",
                        syncItem.Store, syncItem.CategoryName, products?.Count ?? 0);

                    if (products?.Count > 0)
                    {
                        _logger.LogInformation("📦 Nümunələr: {Product1}, {Product2}...",
                            products[0].Name,
                            products.Count > 1 ? products[1].Name : "N/A");
                    }

                    // Cycle üçün yenidən queue-ya əlavə et
                    _syncQueue.Enqueue(syncItem);

                    _logger.LogInformation("➡️ Növbəti kategoriyaya keçir... ({Remaining} qalır)",
                        _syncQueue.Count - 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Category işlənərkən xəta");
            }
            finally
            {
                _isProcessing = false;

                // Kategoriyalar arası qısa fasilə
                await Task.Delay(2000);
            }
        }
    }

    private static void SaveResults(List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto> products, CategorySyncItem syncItem)
    {
        try
        {
            _lastProcessedCategory = $"{syncItem.Store} - {syncItem.CategoryName}";
            _lastProcessedTime = DateTime.Now;

            if (products?.Count > 0)
            {
                _totalProductsProcessed += products.Count;

                // Hər məhsulu təktək əlavə et
                foreach (var product in products)
                {
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
                        imageCount = product.ImageUrl?.Count ?? 0,
                        sizesCount = product.Sizes?.Count ?? 0,
                        colorsCount = product.Colors?.Count ?? 0,
                        store = syncItem.Store,
                        category = syncItem.CategoryName,
                        processedAt = DateTime.Now.ToString("HH:mm:ss")
                    };

                    _allResults.Add(productData);
                }

                _logger.LogInformation("💾 {Count} məhsul saxlandı. Cəmi: {Total}",
                    products.Count, _allResults.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nəticələr saxlanılarkən xəta");
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

    public static object GetAllResults()
    {
        return GetLastResults(); // Eyni nəticə
    }

    // Son işlənən kategoriyanın BÜTÜN məhsulları
    public static object GetLastCategoryResults()
    {
        if (string.IsNullOrEmpty(_lastProcessedCategory))
        {
            return new { message = "Hələ heç bir kategori işlənməyib" };
        }

        var lastCategoryProducts = _allResults
            .Where(p =>
            {
                var categoryProp = p.GetType().GetProperty("category");
                var storeProp = p.GetType().GetProperty("store");
                if (categoryProp != null && storeProp != null)
                {
                    var category = categoryProp.GetValue(p)?.ToString();
                    var store = storeProp.GetValue(p)?.ToString();
                    return $"{store} - {category}" == _lastProcessedCategory;
                }
                return false;
            })
            .ToList();

        return new
        {
            category = _lastProcessedCategory,
            processedAt = _lastProcessedTime.ToString("dd.MM.yyyy HH:mm:ss"),
            totalProducts = lastCategoryProducts.Count,
            products = lastCategoryProducts
        };
    }

    public static object GetStatistics()
    {
        var stats = _allResults
            .GroupBy(p =>
            {
                var categoryProp = p.GetType().GetProperty("category");
                var storeProp = p.GetType().GetProperty("store");
                var category = categoryProp?.GetValue(p)?.ToString() ?? "N/A";
                var store = storeProp?.GetValue(p)?.ToString() ?? "N/A";
                return $"{store} - {category}";
            })
            .Select(g => new
            {
                category = g.Key,
                productCount = g.Count()
            })
            .OrderByDescending(x => x.productCount)
            .ToList();

        return new
        {
            totalProducts = _allResults.Count,
            totalCategories = stats.Count,
            categoriesStats = stats,
            lastUpdate = _lastProcessedTime.ToString("dd.MM.yyyy HH:mm:ss")
        };
    }

    public class CategorySyncItem
    {
        public string CategoryName { get; set; }
        public string Store { get; set; }
    }
}
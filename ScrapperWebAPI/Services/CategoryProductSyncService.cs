namespace ScrapperWebAPI.Services;

using global::ScrapperWebAPI.Helpers.Product;
using ScrapperWebAPI.Helpers.Product;

public class CategoryProductSyncService : BackgroundService
{
    private readonly ILogger<CategoryProductSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromHours(1); // 1 saat

    // Current position tracking
    private int _currentCategoryIndex = 0;
    private List<string> _categoryNames = new List<string>();
    private string _currentStore = "";

    // Service aktivləşdirici
    private bool _isActive = false;
    private TaskCompletionSource<bool> _activationSignal = new TaskCompletionSource<bool>();

    public CategoryProductSyncService(ILogger<CategoryProductSyncService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⏳ Product Sync Service gözləmədə - Categories API manual çağırılsın");

        // Manual activation gözlə
        await _activationSignal.Task;

        _logger.LogInformation("🚀 Product Sync Service aktivləşdi - hər 1 saatda Products API çağırılacaq");

        while (!stoppingToken.IsCancellationRequested && _isActive)
        {
            try
            {
                await ProcessNextCategory();

                _logger.LogInformation("⏰ Növbəti kategori üçün Products API 1 saatdan sonra: {NextRun}", DateTime.Now.AddHours(1));
                await Task.Delay(_syncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Products API sync zamanı xəta baş verdi");
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
    }

    public async Task StartAutoProductSync(List<string> categoryNames, string store)
    {
        if (_isActive)
        {
            _logger.LogInformation("⚠️ Product sync artıq aktivdir");
            return;
        }

        _logger.LogInformation("🎯 Product Auto Sync aktivləşdirilir...");

        _categoryNames = categoryNames ?? new List<string>();
        _currentStore = store;
        _currentCategoryIndex = 0; // İlk kategoriyadan başla

        _logger.LogInformation("📋 Sync parametrləri:");
        _logger.LogInformation("   Store: {Store}", _currentStore);
        _logger.LogInformation("   Kategoriya sayı: {CategoryCount}", _categoryNames.Count);
        _logger.LogInformation("   Kategoriyalar: {Categories}", string.Join(", ", _categoryNames));

        _isActive = true;
        _activationSignal.SetResult(true);
    }

    private async Task ProcessNextCategory()
    {
        try
        {
            if (_categoryNames.Count == 0)
            {
                _logger.LogWarning("⚠️ Heç bir kategori yoxdur");
                return;
            }

            // Hal-hazırki kategori
            var currentCategory = _categoryNames[_currentCategoryIndex];

            _logger.LogInformation("🔄 Products API çağırılır: store={Store}, category={Category} [{Index}/{Total}]",
                _currentStore, currentCategory, _currentCategoryIndex + 1, _categoryNames.Count);

            // Products API-ni çağır
            var products = await CallProductsAPI(_currentStore, currentCategory);

            _logger.LogInformation("✅ Products API cavabı: {Store} - {Category} üçün {Count} məhsul",
                _currentStore, currentCategory, products?.Count ?? 0);

            // Debug məqsədilə ilk məhsulun adını göstər
            if (products?.Count > 0)
            {
                _logger.LogInformation("📦 Nümunə məhsul: {ProductName} - {Price}₼",
                    products[0].Name, products[0].Price);
            }

            // Növbəti kategori indexinə keç
            _currentCategoryIndex++;
            if (_currentCategoryIndex >= _categoryNames.Count)
            {
                _currentCategoryIndex = 0; // Yenidən başla (cycle)
                _logger.LogInformation("🔄 Bütün kategoriyalar tamamlandı, yenidən başlanılır");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Kategori işlənərkən xəta: {Store} - {Category}",
                _currentStore, _currentCategoryIndex < _categoryNames.Count ? _categoryNames[_currentCategoryIndex] : "N/A");

            // Xəta varsa növbəti kategoriyaya keç
            _currentCategoryIndex++;
            if (_currentCategoryIndex >= _categoryNames.Count)
            {
                _currentCategoryIndex = 0;
            }
        }
    }

    private async Task<List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto>> CallProductsAPI(string store, string category)
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
            _logger.LogError(ex, "Products API çağırılarkən xəta: {Store} - {Category}", store, category);
            return new List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto>();
        }
    }

    public string GetCurrentStatus()
    {
        if (!_isActive)
        {
            return "❌ Service aktiv deyil - Categories API manual çağırılmalıdır";
        }

        if (_categoryNames.Count == 0)
        {
            return "⚠️ Kategoriya yoxdur";
        }

        var currentCategory = _currentCategoryIndex < _categoryNames.Count
            ? _categoryNames[_currentCategoryIndex]
            : "N/A";

        return $"🎯 Store: {_currentStore.ToUpper()}, Növbəti kategori: {currentCategory} [{_currentCategoryIndex + 1}/{_categoryNames.Count}]";
    }
}
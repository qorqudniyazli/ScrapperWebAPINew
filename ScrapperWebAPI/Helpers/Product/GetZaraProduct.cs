using Newtonsoft.Json;
using ScrapperWebAPI.Helpers.Mappers;
using ScrapperWebAPI.Models.ProductDtos;
using ScrapperWebAPI.Models.Zara;
using ScrapperWebAPI.Models.Zara.Product;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using ZaraScraperWebApi.Models;

namespace ScrapperWebAPI.Helpers.Product;

public static class GetZaraProduct
{
    private static readonly HttpClient _httpClient;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(20);

    static GetZaraProduct()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 30
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async static Task<List<ProductToListDto>> GetByCategoryName(string category)
    {
        try
        {
            var categoryId = await GetCategoryLink(category);
            if (categoryId == 0)
            {
                return new List<ProductToListDto>();
            }

            string link = CreateLink(categoryId);

            using var apiClient = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            var productLinks = await GetProductLinks(link);
            if (productLinks.Count == 0)
            {
                return new List<ProductToListDto>();
            }

            const int pageSize = 50;
            var allProducts = new List<ProductToListDto>();

            for (int i = 0; i < productLinks.Count; i += pageSize)
            {
                var currentBatch = productLinks.Skip(i).Take(pageSize).ToList();
                var pageNumber = (i / pageSize) + 1;

                var pageProducts = await ProcessProductBatch(currentBatch, pageNumber);

                if (pageProducts.Count > 0)
                {
                    await SendPageToExternalAPI(pageProducts, category, pageNumber, apiClient);
                    allProducts.AddRange(pageProducts);
                }

                await Task.Delay(1000);
            }

            return allProducts;
        }
        catch (Exception ex)
        {
            return new List<ProductToListDto>();
        }
    }

    private static async Task<List<ProductToListDto>> ProcessProductBatch(List<string> productLinks, int pageNumber)
    {
        var productDetails = new ConcurrentBag<ProductToListDto>();

        var tasks = productLinks.Select(async seoLink =>
        {
            await _semaphore.WaitAsync();
            try
            {
                var product = await ProcessProductLink(seoLink);
                if (product != null)
                {
                    productDetails.Add(product);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return productDetails.ToList();
    }

    private static async Task SendPageToExternalAPI(List<ProductToListDto> products, string category, int pageNumber, HttpClient apiClient)
    {
        try
        {
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
                    productUrl = product.ProductUrl,
                    discountedPrice = product.DiscountedPrice,
                    description = !string.IsNullOrEmpty(product.Description) && product.Description.Length > 150
                        ? product.Description.Substring(0, 150) + "..."
                        : product.Description ?? "",
                    images = product.ImageUrl ?? new List<string>(),
                    sizes = sizes,
                    colors = colors,
                    store = "zara",
                    category = category,
                    processedAt = DateTime.Now.ToString("HH:mm:ss")
                };

                productsForAPI.Add(productData);
            }

            const int batchSize = 20;
            var batches = ChunkList(productsForAPI, batchSize);

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

                        var response = await apiClient.PostAsync(
                            "http://69.62.114.202:5009/api/v1/products-stock/add-products",
                            content);

                        if (response.IsSuccessStatusCode)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (retry < maxRetries - 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5 * (retry + 1)));
                        }
                    }
                }

                if (i < batches.Count - 1)
                {
                    await Task.Delay(2000);
                }
            }
        }
        catch (Exception ex)
        {
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

    private static async Task<HashSet<string>> GetProductLinks(string link)
    {
        try
        {
            var response = await GetWithRetry(link);
            if (response == null)
            {
                return new HashSet<string>();
            }

            var result = JsonConvert.DeserializeObject<ZaraRootSeo>(response);
            HashSet<string> links = new HashSet<string>();

            if (result?.ProductGroups != null)
            {
                foreach (var productGroup in result.ProductGroups)
                {
                    if (productGroup.Elements != null)
                    {
                        foreach (var element in productGroup.Elements)
                        {
                            if (element.CommercialComponents != null)
                            {
                                foreach (var component in element.CommercialComponents)
                                {
                                    if (component.Seo != null &&
                                        !string.IsNullOrEmpty(component.Seo.Keyword) &&
                                        !string.IsNullOrEmpty(component.Seo.SeoProductId))
                                    {
                                        var seo = $"https://www.zara.com/az/ru/{component.Seo.Keyword}-p{component.Seo.SeoProductId}.html?ajax=true";
                                        links.Add(seo);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return links;
        }
        catch (Exception ex)
        {
            return new HashSet<string>();
        }
    }

    private static async Task<ProductToListDto> ProcessProductLink(string seoLink)
    {
        try
        {
            var linkJson = await GetWithRetry(seoLink);
            if (linkJson == null)
            {
                return null;
            }

            var productDetail = JsonConvert.DeserializeObject<Root>(linkJson);
            if (productDetail != null)
            {
                var dto = RootMapper.MapToDto(productDetail);
                if (dto != null)
                {
                    return dto;
                }
            }
        }
        catch (JsonException jsonEx)
        {
        }
        catch (Exception ex)
        {
        }

        return null;
    }

    private static async Task<string> GetWithRetry(string url, int maxRetries = 3)
    {
        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    if (i == maxRetries)
                        return null;
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (i == maxRetries)
                    return null;
            }
            catch (TaskCanceledException timeoutEx)
            {
                if (i == maxRetries)
                    return null;
            }
            catch (Exception ex)
            {
                if (i == maxRetries)
                    return null;
            }

            if (i < maxRetries)
            {
                await Task.Delay(200 * (i + 1));
            }
        }

        return null;
    }

    public async static Task<long> GetCategoryLink(string category)
    {
        try
        {
            var parsed = ParseMenu(category);
            if (string.IsNullOrEmpty(parsed.Sub))
            {
                return 0;
            }

            string url = "https://www.zara.com/az/ru/categories?categoryId=2536906&categorySeoId=640&ajax=true";

            var json = await GetWithRetry(url);
            if (json == null)
            {
                return 0;
            }

            ZaraCategoryRoot data = JsonConvert.DeserializeObject<ZaraCategoryRoot>(json);
            if (data?.Categories == null)
            {
                return 0;
            }

            foreach (var cat in data.Categories)
            {
                if (cat.Subcategories != null && cat.Subcategories.Count > 0)
                {
                    foreach (var sub in cat.Subcategories)
                    {
                        if (string.Equals(sub.Name, parsed.Sub, StringComparison.OrdinalIgnoreCase))
                        {
                            long subcategoryId = sub.Id;

                            if (sub.Subcategories != null && sub.Subcategories.Count > 0)
                            {
                                subcategoryId = sub.Subcategories[0].Id;
                            }

                            return subcategoryId;
                        }
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return 0;
        }
    }

    public static (string Main, string Sub) ParseMenu(string input)
    {
        if (string.IsNullOrEmpty(input))
            return (null, null);

        var parts = input.Split('-', 2);

        string main = parts.Length > 0 ? parts[0] : null;
        string sub = parts.Length > 1 ? parts[1] : null;

        return (main, sub);
    }

    private static string CreateLink(long id)
    {
        return $"https://www.zara.com/az/ru/category/{id}/products?ajax=true";
    }

    public static void Dispose()
    {
        _httpClient?.Dispose();
        _semaphore?.Dispose();
    }
}
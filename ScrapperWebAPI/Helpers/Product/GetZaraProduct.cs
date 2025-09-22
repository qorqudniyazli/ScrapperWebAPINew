using Newtonsoft.Json;
using ScrapperWebAPI.Helpers.Mappers;
using ScrapperWebAPI.Models.ProductDtos;
using ScrapperWebAPI.Models.Zara;
using ScrapperWebAPI.Models.Zara.Product;
using System.Collections.Concurrent;
using System.Net.Http;
using ZaraScraperWebApi.Models;

namespace ScrapperWebAPI.Helpers.Product;

public static class GetZaraProduct
{
    private static readonly HttpClient _httpClient;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(20); // 20 parallel requests

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
                Console.WriteLine($"Category '{category}' not found");
                return new List<ProductToListDto>();
            }

            string link = CreateLink(categoryId);

            // Get product links
            var productLinks = await GetProductLinks(link);
            if (productLinks.Count == 0)
            {
                Console.WriteLine("No product links found");
                return new List<ProductToListDto>();
            }

            Console.WriteLine($"Found {productLinks.Count} product links. Starting parallel processing...");

            // PARALLEL PROCESSING - Key optimization
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

            Console.WriteLine($"Successfully processed {productDetails.Count} products");
            return productDetails.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetByCategoryName: {ex.Message}");
            return new List<ProductToListDto>();
        }
    }

    private static async Task<HashSet<string>> GetProductLinks(string link)
    {
        try
        {
            var response = await GetWithRetry(link);
            if (response == null)
            {
                Console.WriteLine($"Failed to get category data from: {link}");
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
            Console.WriteLine($"Error getting product links: {ex.Message}");
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
                Console.WriteLine($"Failed to get product data from: {seoLink}");
                return null;
            }

            var productDetail = JsonConvert.DeserializeObject<Root>(linkJson);
            if (productDetail != null)
            {
                var dto = RootMapper.MapToDto(productDetail);
                if (dto != null)
                {
                    Console.WriteLine($"Successfully processed product: {dto.Name}");
                    return dto;
                }
            }
        }
        catch (JsonException jsonEx)
        {
            Console.WriteLine($"JSON parsing error for {seoLink}: {jsonEx.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing product link {seoLink}: {ex.Message}");
        }

        return null;
    }

    private static async Task<string> GetWithRetry(string url, int maxRetries = 2)
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
                    Console.WriteLine($"HTTP {response.StatusCode} for {url}");
                    if (i == maxRetries)
                        return null;
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP error (attempt {i + 1}): {httpEx.Message}");
                if (i == maxRetries)
                    return null;
            }
            catch (TaskCanceledException timeoutEx)
            {
                Console.WriteLine($"Timeout error (attempt {i + 1}): {timeoutEx.Message}");
                if (i == maxRetries)
                    return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error (attempt {i + 1}): {ex.Message}");
                if (i == maxRetries)
                    return null;
            }

            if (i < maxRetries)
            {
                await Task.Delay(200 * (i + 1)); // Progressive delay
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
                Console.WriteLine($"Invalid category format: {category}. Expected format: 'main-sub'");
                return 0;
            }

            string url = "https://www.zara.com/az/ru/categories?categoryId=2536906&categorySeoId=640&ajax=true";

            var json = await GetWithRetry(url);
            if (json == null)
            {
                Console.WriteLine("Failed to get category data");
                return 0;
            }

            ZaraCategoryRoot data = JsonConvert.DeserializeObject<ZaraCategoryRoot>(json);
            if (data?.Categories == null)
            {
                Console.WriteLine("Invalid category data structure");
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

                            // If nested subcategory exists, take the first one
                            if (sub.Subcategories != null && sub.Subcategories.Count > 0)
                            {
                                subcategoryId = sub.Subcategories[0].Id;
                            }

                            Console.WriteLine($"Found category '{parsed.Sub}' with ID: {subcategoryId}");
                            return subcategoryId;
                        }
                    }
                }
            }

            Console.WriteLine($"Category '{parsed.Sub}' not found");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting category link: {ex.Message}");
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

    // Cleanup method for proper disposal
    public static void Dispose()
    {
        _httpClient?.Dispose();
        _semaphore?.Dispose();
    }
}
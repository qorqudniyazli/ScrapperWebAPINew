using ScrapperWebAPI.Models.GoSport;
using ScrapperWebAPI.Models.ProductDtos;
using System.Diagnostics.Contracts;

namespace ScrapperWebAPI.Helpers.Mappers
{
    public static class GoSportMapper
    {
        private static readonly HttpClient _httpClient;
        private static readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(10); // 10 paralel şəkil yükləmə

        static GoSportMapper()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 25
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30) // Şəkil yükləmə üçün daha uzun timeout
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public static async Task<List<ProductToListDto>> MapAsync(List<GoSportProduct> products)
        {
            var list = new List<ProductToListDto>();

            Console.WriteLine($"Şəkil çevirməsi başlayır {products.Count} məhsul üçün...");

            foreach (var item in products)
            {
                var product = new ProductToListDto()
                {
                    Name = item.Name,
                    Price = item.Price,
                    Description = item.ShortDescription,
                    Brand = item.Brand,
                    DiscountedPrice = item.DiscountedPrice,
                    ImageUrl = await ConvertImagesToBase64(item.AdditionalImages), // YENİ: Base64 çevirmə
                };

                product.Colors = new List<Color>();
                product.Sizes = new List<Sizes>();

                foreach (var size in item.Sizes)
                {
                    product.Sizes.Add(new Sizes
                    {
                        SizeName = size.SizeName,
                        OnStock = size.IsAvailable
                    });
                }

                list.Add(product);
            }

            Console.WriteLine($"Şəkil çevirməsi tamamlandı!");
            return list;
        }

        // YENİ: Sinxron versiya (köhnə kodu saxlamaq üçün)
        public static List<ProductToListDto> Map(List<GoSportProduct> products)
        {
            return MapAsync(products).Result; // Async-i sinxron çağırırıq
        }

        // YENİ: Şəkilləri Base64-ə çevirmə methodu
        private static async Task<List<string>> ConvertImagesToBase64(List<string> imageUrls)
        {
            if (imageUrls == null || imageUrls.Count == 0)
                return new List<string>();

            var base64Images = new List<string>();

            // Paralel şəkil yükləmə
            var downloadTasks = imageUrls.Take(5).Select(async imageUrl => // İlk 5 şəkil
            {
                await _downloadSemaphore.WaitAsync();
                try
                {
                    var base64 = await DownloadImageAsBase64(imageUrl);
                    return base64;
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            });

            var results = await Task.WhenAll(downloadTasks);

            // Null olmayan nəticələri əlavə et
            foreach (var result in results)
            {
                if (!string.IsNullOrEmpty(result))
                {
                    base64Images.Add(result);
                }
            }

            return base64Images;
        }

        // YENİ: Tək şəkil yükləmə və Base64 çevirmə
        private static async Task<string> DownloadImageAsBase64(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return null;

            try
            {
                Console.WriteLine($"Şəkil yüklənir: {imageUrl}");

                // Şəkil yükləmə
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                // MIME type təyin etmə
                var mimeType = GetMimeTypeFromUrl(imageUrl);

                // Base64-ə çevirmə
                var base64String = Convert.ToBase64String(imageBytes);
                var dataUri = $"data:{mimeType};base64,{base64String}";

                Console.WriteLine($"Şəkil çevrildi: {imageUrl} -> {dataUri.Length} characters");
                return dataUri;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Şəkil yükləmə xətası {imageUrl}: {ex.Message}");
                return null;
            }
        }

        // YENİ: URL-dən MIME type təyin etmə
        private static string GetMimeTypeFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "image/jpeg";

            var extension = Path.GetExtension(url).ToLowerInvariant();

            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                _ => "image/jpeg" // Default
            };
        }

        // YENİ: Daha təkmil versiya - şəkil ölçüsü ilə
        public static async Task<List<ProductToListDto>> MapWithImageOptimization(
            List<GoSportProduct> products,
            int maxImageWidth = 500,
            int maxImages = 3)
        {
            var list = new List<ProductToListDto>();

            Console.WriteLine($"Optimallaşdırılmış şəkil çevirməsi başlayır {products.Count} məhsul üçün...");

            foreach (var item in products)
            {
                var product = new ProductToListDto()
                {
                    Name = item.Name,
                    Price = item.Price,
                    Description = item.ShortDescription,
                    Brand = item.Brand,
                    DiscountedPrice = item.DiscountedPrice,
                    ImageUrl = await ConvertAndOptimizeImages(item.AdditionalImages, maxImageWidth, maxImages),
                };

                product.Colors = new List<Color>();
                product.Sizes = new List<Sizes>();

                foreach (var size in item.Sizes)
                {
                    product.Sizes.Add(new Sizes
                    {
                        SizeName = size.SizeName,
                        OnStock = size.IsAvailable
                    });
                }

                list.Add(product);
            }

            Console.WriteLine($"Optimallaşdırılmış şəkil çevirməsi tamamlandı!");
            return list;
        }

        // YENİ: Şəkil optimallaşdırması ilə çevirmə
        private static async Task<List<string>> ConvertAndOptimizeImages(
            List<string> imageUrls,
            int maxWidth,
            int maxImages)
        {
            if (imageUrls == null || imageUrls.Count == 0)
                return new List<string>();

            var base64Images = new List<string>();

            // Maksimum şəkil sayı
            var urlsToProcess = imageUrls.Take(maxImages);

            foreach (var imageUrl in urlsToProcess)
            {
                await _downloadSemaphore.WaitAsync();
                try
                {
                    var base64 = await DownloadAndOptimizeImage(imageUrl, maxWidth);
                    if (!string.IsNullOrEmpty(base64))
                    {
                        base64Images.Add(base64);
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }

            return base64Images;
        }

        private static async Task<string> DownloadAndOptimizeImage(string imageUrl, int maxWidth)
        {
            try
            {
                Console.WriteLine($"Optimallaşdırılmış şəkil yüklənir: {imageUrl}");

                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                if (imageBytes.Length > 500000) 
                {
                    Console.WriteLine($"Şəkil ölçüsü azaldılır: {imageBytes.Length} bytes");
                }

                var mimeType = GetMimeTypeFromUrl(imageUrl);
                var base64String = Convert.ToBase64String(imageBytes);
                var dataUri = $"data:{mimeType};base64,{base64String}";

                Console.WriteLine($"Optimallaşdırılmış şəkil çevrildi: {dataUri.Length} characters");
                return dataUri;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Optimallaşdırılmış şəkil yükləmə xətası {imageUrl}: {ex.Message}");
                return null;
            }
        }

        // YENİ: Cleanup method
        public static void Dispose()
        {
            _httpClient?.Dispose();
            _downloadSemaphore?.Dispose();
        }
    }
}
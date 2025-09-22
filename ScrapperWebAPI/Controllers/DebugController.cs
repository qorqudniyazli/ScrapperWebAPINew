using Microsoft.AspNetCore.Mvc;
using ScrapperWebAPI.Helpers.Product;

namespace ScrapperWebAPI.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class DebugController : ControllerBase
{
    [HttpGet("test-images")]
    public async Task<IActionResult> TestImages(string store = "gosport", string category = "NIKE")
    {
        try
        {
            Console.WriteLine($"🔍 Testing images for: {store} - {category}");

            List<ScrapperWebAPI.Models.ProductDtos.ProductToListDto> products;

            if (store.ToLower() == "gosport")
            {
                products = await GetGoSportProducts.GetByProductByBrand(category);
            }
            else
            {
                products = await GetZaraProduct.GetByCategoryName(category);
            }

            if (products == null || products.Count == 0)
            {
                return Ok(new { message = "Məhsul tapılmadı", store, category });
            }

            // İlk məhsulun şəkil məlumatlarını detailed yoxla
            var firstProduct = products[0];

            var debugInfo = new
            {
                productName = firstProduct.Name,
                imageUrlType = firstProduct.ImageUrl?.GetType().Name,
                imageUrlCount = firstProduct.ImageUrl?.Count ?? 0,
                imageUrls = firstProduct.ImageUrl?.Take(3).ToList(), // İlk 3 şəkil
                hasImages = firstProduct.ImageUrl != null && firstProduct.ImageUrl.Count > 0,

                // Debug üçün
                allProducts = products.Take(2).Select(p => new
                {
                    name = p.Name,
                    price = p.Price,
                    imageCount = p.ImageUrl?.Count ?? 0,
                    firstImagePreview = p.ImageUrl?.FirstOrDefault()?.Substring(0, Math.Min(50, p.ImageUrl.FirstOrDefault()?.Length ?? 0)) + "...",
                    hasImageUrl = p.ImageUrl != null,
                    imageUrlIsEmpty = p.ImageUrl?.Count == 0
                }).ToList()
            };

            return Ok(debugInfo);
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}
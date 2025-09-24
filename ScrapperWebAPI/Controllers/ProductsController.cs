using Microsoft.AspNetCore.Mvc;
using ScrapperWebAPI.Helpers;
using ScrapperWebAPI.Helpers.Product;
using ScrapperWebAPI.Models.ProductDtos;

namespace ScrapperWebAPI.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string store, string category)
    {
        if (store.ToLower() == "gosport")
        {
            var data = await GetGoSportProducts.GetByProductByBrand(category);
            var limitedData = data.Take(24).ToList();

            return Ok(limitedData);
        }
        else if (store.ToLower() == "zara")
        {
            var data = await GetZaraProduct.GetByCategoryName(category);
            var limitedData = data.Take(50).ToList();

            return Ok(limitedData);
        }

        return BadRequest("This store can not be found");
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll(string? store)
    {
        var list = new HashSet<ProductToListDto>();

        if (store.ToLower() == "zara")
        {
            var categories = await GetZaraCategories.GetAll();

            foreach (var category in categories)
            {
                var products = await GetZaraProduct.GetByCategoryName(category.Name);
                // Hər kategori üçün ilk 30 məhsul
                var limitedProducts = products.Take(30);

                foreach (var product in limitedProducts)
                {
                    list.Add(product);
                }
            }
        }

        if (store.ToLower() == "gosport")
        {
            var brands = await GetGoSportBrands.GetAll();

            foreach (var brand in brands)
            {
                var products = await GetGoSportProducts.GetByProductByBrand(brand.Name);
                // Hər brand üçün ilk 24 məhsul
                var limitedProducts = products.Take(24);

                foreach (var product in limitedProducts)
                {
                    list.Add(product);
                }
            }
        }

        return Ok(list);
    }
}
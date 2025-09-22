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
            return Ok(data);
        }
        else if (store.ToLower() == "zara")
        {
            var data = await GetZaraProduct.GetByCategoryName(category);
            return Ok(data);
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
                foreach (var product in products)
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
                foreach (var product in products)
                {
                    list.Add(product);
                }
            }
        }

        return Ok(list);

    }
}

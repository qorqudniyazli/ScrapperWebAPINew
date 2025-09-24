using Microsoft.AspNetCore.Mvc;
using ScrapperWebAPI.Helpers;
using ScrapperWebAPI.Services;
using System.Text;
using Newtonsoft.Json;

namespace ScrapperWebAPI.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class CategoriesController : ControllerBase
{
    private readonly HttpClient _httpClient;

    public CategoriesController(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string store)
    {
        try
        {
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
                        store = store // Store field-i əlavə edilir
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
                        store = store // Store field-i əlavə edilir
                    });
                }
            }
            else
            {
                return BadRequest("Store not found");
            }

            if (dataToSend.Count > 0)
            {
                await SendToExternalApi(dataToSend);

                await StartProductSync(dataToSend, store);
            }

            return Ok(dataToSend);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in Categories GET: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task SendToExternalApi(List<object> data)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "http://69.62.114.202:5009/api/v1/category-stock/add-category",
                content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Data successfully sent to external API");
            }
            else
            {
                Console.WriteLine($"Failed to send data to external API. Status: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error response: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception sending data to external API: {ex.Message}");
        }
    }

    private async Task StartProductSync(List<object> categories, string store)
    {
        try
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

            Console.WriteLine($"🚀 Static sync başladılır: {store} üçün {categoryNames.Count} kategori");

            ProductSyncManager.StartAutoSync(categoryNames, store);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting product sync: {ex.Message}");
        }
    }
}
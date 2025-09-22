using ZaraScraperApi.Controllers;
using ScrapperWebAPI.Services; // Yeni service üçün

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // <-- Zara üçün lazım idi
builder.Logging.AddConsole();

// 🔹 CORS policy əlavə et
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()   // bütün domenlər
              .AllowAnyMethod()   // GET, POST, PUT, DELETE hamısına icazə
              .AllowAnyHeader();  // bütün header-lərə icazə
    });
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ZaraController>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Static service istifadə olunur, background service qeydiyyatı lazım deyil

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// 🔹 CORS istifadə et
app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.Run();
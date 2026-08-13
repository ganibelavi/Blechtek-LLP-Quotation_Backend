using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using QuotationApp.API.Data;
using QuotationApp.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<QuotationSettings>(builder.Configuration.GetSection("QuotationSettings"));

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\mssqllocaldb;Database=Quotation_LLP_Db;Trusted_Connection=True;MultipleActiveResultSets=true";
builder.Services.AddDbContext<QuotationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Use SQL-backed services (replace JSON-file-based services)
// builder.Services.AddSingleton<IModuleService, ModuleService>(); // JSON-based
builder.Services.AddScoped<IModuleService, SqlModuleService>(); // SQL-based
builder.Services.AddScoped<IWordGeneratorService, WordGeneratorService>();
builder.Services.AddScoped<IPdfConverterService, PdfConverterService>();
builder.Services.AddScoped<ITemplateService, TemplateService>(); // Add TemplateService
// builder.Services.AddScoped<IQuotationService, QuotationService>(); // JSON-based
builder.Services.AddScoped<IQuotationService, SqlQuotationService>(); // SQL-based
// Add user service for authentication
builder.Services.AddScoped<IUserService, UserService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

// Configure JWT authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key");
if (!string.IsNullOrEmpty(jwtKey))
{
    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection.GetValue<string>("Issuer"),
            ValidAudience = jwtSection.GetValue<string>("Audience"),
            IssuerSigningKey = signingKey
        };
    });
}

var app = builder.Build();

// Ensure database is created and seeded
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
    var moduleService = scope.ServiceProvider.GetRequiredService<IModuleService>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var settings = scope.ServiceProvider.GetRequiredService<IOptions<QuotationSettings>>();
    var templateService = scope.ServiceProvider.GetRequiredService<ITemplateService>(); // Get TemplateService

    dbContext.Database.EnsureCreated();

    // Update template footer on startup (safe way using DocX)
    var templatePath = Path.Combine(env.ContentRootPath, settings.Value.TemplatePath);
    templateService.UpdateFooter(templatePath);

    // Seed modules from JSON file if database is empty
    if (moduleService is SqlModuleService sqlModuleService)
    {
        var modulesFile = Path.Combine(env.ContentRootPath, settings.Value.ModulesFile);
        await sqlModuleService.SeedFromJsonAsync(modulesFile);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

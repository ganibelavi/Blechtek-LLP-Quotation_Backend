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

// Modules are managed through the SQL database and exposed by /api/modules.
builder.Services.AddScoped<IModuleService, SqlModuleService>();
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

// Ensure database is created and seed data is available; also add any recent schema columns
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
    dbContext.Database.EnsureCreated();

    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        connection.Open();
    }

    using var command = connection.CreateCommand();
    command.CommandText = @"SELECT CASE WHEN EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'Quotations' AND COLUMN_NAME = 'CreatedByUser'
    ) THEN 1 ELSE 0 END";

    var hasCreatedByUser = Convert.ToInt32(command.ExecuteScalar()) == 1;
    if (!hasCreatedByUser)
    {
        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE dbo.Quotations ADD CreatedByUser nvarchar(200) NULL;";
        alterCommand.ExecuteNonQuery();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

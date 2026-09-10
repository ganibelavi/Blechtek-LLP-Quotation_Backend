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
// builder.Services.AddScoped<IWordGeneratorService, WordGeneratorService>();
builder.Services.AddScoped<IPdfConverterService, PdfConverterService>();
builder.Services.AddScoped<ITemplateService, TemplateService>(); // Add TemplateService
// builder.Services.AddScoped<IQuotationService, QuotationService>(); // JSON-based
builder.Services.AddScoped<IQuotationService, SqlQuotationService>(); // SQL-based
// Add user service for authentication
builder.Services.AddScoped<IUserService, UserService>();

// Configure SMTP email options and register email service
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

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

    using var historyTableCommand = connection.CreateCommand();
    historyTableCommand.CommandText = @"
IF OBJECT_ID(N'dbo.QuotationHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QuotationHistory
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuotationHistory PRIMARY KEY,
        QuotationId nvarchar(50) NOT NULL,
        OrganizationName nvarchar(200) NOT NULL,
        QuotationNo nvarchar(50) NULL,
        Date datetime2 NULL,
        ValidationDate datetime2 NOT NULL,
        ReferenceBy nvarchar(150) NULL,
        QuotationToName nvarchar(150) NOT NULL,
        QuotationToAddress nvarchar(400) NOT NULL,
        QuotationToContactNo nvarchar(30) NOT NULL,
        QuotationToEmail nvarchar(150) NOT NULL,
        ModulesJson nvarchar(max) NOT NULL,
        DiscountPercentage decimal(5,2) NULL,
        ChangedAt datetime2 NOT NULL,
        ChangeType nvarchar(30) NOT NULL
    );
    CREATE INDEX IX_QuotationHistory_QuotationId ON dbo.QuotationHistory (QuotationId);
    CREATE INDEX IX_QuotationHistory_Organization ON dbo.QuotationHistory (OrganizationName);
END";
    historyTableCommand.ExecuteNonQuery();

    using var masterTablesCommand = connection.CreateCommand();
    masterTablesCommand.CommandText = @"
IF OBJECT_ID(N'dbo.company_profile', N'U') IS NULL
CREATE TABLE dbo.company_profile (id int IDENTITY(1,1) PRIMARY KEY, name varchar(255) NOT NULL, address varchar(1000) NULL, state varchar(100) NULL, state_code varchar(10) NULL, gstn varchar(20) NULL, default_terms_of_sale nvarchar(max) NULL, is_active bit NOT NULL DEFAULT 1);
IF OBJECT_ID(N'dbo.company_bank_accounts', N'U') IS NULL
CREATE TABLE dbo.company_bank_accounts (id int IDENTITY(1,1) PRIMARY KEY, bank_name varchar(255) NULL, account_no varchar(100) NULL, account_type varchar(50) NOT NULL DEFAULT 'Current', ifsc varchar(50) NULL, msme_no varchar(100) NULL, is_default bit NOT NULL DEFAULT 0, is_active bit NOT NULL DEFAULT 1);
IF OBJECT_ID(N'dbo.gst_rates', N'U') IS NULL
CREATE TABLE dbo.gst_rates (id int IDENTITY(1,1) PRIMARY KEY, label varchar(100) NOT NULL, sgst_pct decimal(5,2) NOT NULL DEFAULT 0, cgst_pct decimal(5,2) NOT NULL DEFAULT 0, igst_pct decimal(5,2) NOT NULL DEFAULT 0, is_active bit NOT NULL DEFAULT 1, created_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID(N'dbo.terms_templates', N'U') IS NULL
CREATE TABLE dbo.terms_templates (id int IDENTITY(1,1) PRIMARY KEY, type varchar(30) NOT NULL, label varchar(150) NOT NULL, content nvarchar(max) NOT NULL, is_default bit NOT NULL DEFAULT 0, is_active bit NOT NULL DEFAULT 1, created_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
";
    masterTablesCommand.ExecuteNonQuery();

    using var renewalTablesCommand = connection.CreateCommand();
    renewalTablesCommand.CommandText = @"
IF OBJECT_ID(N'dbo.ModulePricing', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModulePricing (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModulePricing PRIMARY KEY,
        ModuleId int NOT NULL,
        InitialPurchasePrice decimal(12,2) NOT NULL,
        RenewalPercentage decimal(5,2) NOT NULL,
        AnnualEscalationPercentage decimal(5,2) NOT NULL,
        PricingEffectiveFrom date NOT NULL,
        PricingEffectiveTo date NULL,
        IsActive bit NOT NULL,
        CreatedAt datetime2 NOT NULL,
        CONSTRAINT FK_ModulePricing_Modules FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(Id)
    );
    CREATE INDEX IX_ModulePricing_ModuleId ON dbo.ModulePricing(ModuleId);
END;
IF OBJECT_ID(N'dbo.CustomerModuleSubscription', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerModuleSubscription (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerModuleSubscription PRIMARY KEY,
        CustomerId int NOT NULL,
        ModuleId int NOT NULL,
        QuotationId nvarchar(50) NULL,
        PurchaseDate date NOT NULL,
        SubscriptionStartDate date NOT NULL,
        SubscriptionEndDate date NULL,
        CurrentYear int NULL,
        InitialPurchasePrice decimal(12,2) NOT NULL,
        RenewalPercentage decimal(5,2) NOT NULL,
        AnnualEscalationPercentage decimal(5,2) NOT NULL,
        Status nvarchar(20) NOT NULL,
        NextRenewalDate date NULL,
        CreatedAt datetime2 NOT NULL,
        CONSTRAINT FK_CustomerModuleSubscription_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.customers(id),
        CONSTRAINT FK_CustomerModuleSubscription_Modules FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(Id),
        CONSTRAINT FK_CustomerModuleSubscription_Quotations FOREIGN KEY (QuotationId) REFERENCES dbo.Quotations(Id)
    );
    CREATE INDEX IX_CustomerModuleSubscription_CustomerId ON dbo.CustomerModuleSubscription(CustomerId);
    CREATE INDEX IX_CustomerModuleSubscription_ModuleId ON dbo.CustomerModuleSubscription(ModuleId);
    CREATE INDEX IX_CustomerModuleSubscription_Status ON dbo.CustomerModuleSubscription(Status);
END;
IF OBJECT_ID(N'dbo.SubscriptionRenewal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SubscriptionRenewal (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SubscriptionRenewal PRIMARY KEY,
        SubscriptionId int NOT NULL,
        RenewalYear int NOT NULL,
        PeriodStartDate date NOT NULL,
        PeriodEndDate date NOT NULL,
        PreviousAmount decimal(12,2) NOT NULL,
        EscalationPercentage decimal(5,2) NOT NULL,
        RenewalAmount decimal(12,2) NOT NULL,
        QuotationId nvarchar(50) NULL,
        InvoiceId int NULL,
        Status nvarchar(20) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        CONSTRAINT FK_SubscriptionRenewal_Subscriptions FOREIGN KEY (SubscriptionId) REFERENCES dbo.CustomerModuleSubscription(Id) ON DELETE CASCADE,
        CONSTRAINT FK_SubscriptionRenewal_Quotations FOREIGN KEY (QuotationId) REFERENCES dbo.Quotations(Id),
        CONSTRAINT FK_SubscriptionRenewal_Invoices FOREIGN KEY (InvoiceId) REFERENCES dbo.invoices(id),
        CONSTRAINT UQ_SubscriptionRenewal_Subscription_Year UNIQUE (SubscriptionId, RenewalYear)
    );
    CREATE INDEX IX_SubscriptionRenewal_SubscriptionId ON dbo.SubscriptionRenewal(SubscriptionId);
    CREATE INDEX IX_SubscriptionRenewal_Status ON dbo.SubscriptionRenewal(Status);
END";
    renewalTablesCommand.ExecuteNonQuery();

    using var customerSchemaCommand = connection.CreateCommand();
    customerSchemaCommand.CommandText = @"
IF OBJECT_ID(N'dbo.customers', N'U') IS NOT NULL
BEGIN
IF COL_LENGTH(N'dbo.customers', N'contact_name') IS NULL
    ALTER TABLE dbo.customers ADD contact_name varchar(150) NULL;
IF COL_LENGTH(N'dbo.customers', N'contact_number') IS NULL
    ALTER TABLE dbo.customers ADD contact_number varchar(30) NULL;
IF COL_LENGTH(N'dbo.customers', N'email') IS NULL
    ALTER TABLE dbo.customers ADD email varchar(255) NULL;
END";
    customerSchemaCommand.ExecuteNonQuery();

    using var purchaseOrderSchemaCommand = connection.CreateCommand();
    purchaseOrderSchemaCommand.CommandText = @"
IF OBJECT_ID(N'dbo.purchase_orders', N'U') IS NOT NULL
BEGIN
IF COL_LENGTH(N'dbo.purchase_orders', N'quotation_id') IS NULL
    ALTER TABLE dbo.purchase_orders ADD quotation_id nvarchar(50) NULL;
ELSE
BEGIN
    DECLARE @quotationForeignKeys nvarchar(max) = N'';
    SELECT @quotationForeignKeys = @quotationForeignKeys
        + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
        + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
        + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
    FROM sys.foreign_keys fk
    INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.purchase_orders')
      AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'quotation_id';
    IF @quotationForeignKeys <> N'' EXEC sp_executesql @quotationForeignKeys;
    ALTER TABLE dbo.purchase_orders ALTER COLUMN quotation_id nvarchar(50) NULL;
END;
IF COL_LENGTH(N'dbo.purchase_orders', N'po_direction') IS NULL
    ALTER TABLE dbo.purchase_orders ADD po_direction varchar(20) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'received_from_email') IS NULL
    ALTER TABLE dbo.purchase_orders ADD received_from_email varchar(255) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'attachment_url') IS NULL
    ALTER TABLE dbo.purchase_orders ADD attachment_url varchar(1000) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'verification_status') IS NULL
    ALTER TABLE dbo.purchase_orders ADD verification_status varchar(30) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'verified_by') IS NULL
    ALTER TABLE dbo.purchase_orders ADD verified_by varchar(200) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'verified_at') IS NULL
    ALTER TABLE dbo.purchase_orders ADD verified_at datetime2 NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'verification_notes') IS NULL
    ALTER TABLE dbo.purchase_orders ADD verification_notes varchar(max) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'uploaded_by') IS NULL
    ALTER TABLE dbo.purchase_orders ADD uploaded_by varchar(200) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'received_at') IS NULL
    ALTER TABLE dbo.purchase_orders ADD received_at datetime2 NULL;

DECLARE @auditForeignKeys nvarchar(max) = N'';
SELECT @auditForeignKeys = @auditForeignKeys
    + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
    + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
    + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
WHERE fk.parent_object_id = OBJECT_ID(N'dbo.purchase_orders')
  AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) IN (N'verified_by', N'uploaded_by');
IF @auditForeignKeys <> N'' EXEC sp_executesql @auditForeignKeys;

IF COL_LENGTH(N'dbo.purchase_orders', N'verified_by') IS NOT NULL
    ALTER TABLE dbo.purchase_orders ALTER COLUMN verified_by nvarchar(200) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'uploaded_by') IS NOT NULL
    ALTER TABLE dbo.purchase_orders ALTER COLUMN uploaded_by nvarchar(200) NULL;
END";
    purchaseOrderSchemaCommand.ExecuteNonQuery();

    using var moduleSchemaCommand = connection.CreateCommand();
    moduleSchemaCommand.CommandText = @"
IF OBJECT_ID(N'dbo.Modules', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Modules', N'HsnCode') IS NULL
        ALTER TABLE dbo.Modules ADD HsnCode nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.Modules', N'SacCode') IS NULL
        ALTER TABLE dbo.Modules ADD SacCode nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.Modules', N'ReverseChargeDefault') IS NULL
        ALTER TABLE dbo.Modules ADD ReverseChargeDefault bit NOT NULL CONSTRAINT DF_Modules_ReverseChargeDefault DEFAULT 0;
    IF COL_LENGTH(N'dbo.Modules', N'ImplementationEffortCost') IS NULL
        ALTER TABLE dbo.Modules ADD ImplementationEffortCost decimal(18,2) NULL;
END";
    moduleSchemaCommand.ExecuteNonQuery();

    using var invoiceSchemaCommand = connection.CreateCommand();
    invoiceSchemaCommand.CommandText = @"
IF OBJECT_ID(N'dbo.invoices', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.invoices', N'company_profile_id') IS NULL
        ALTER TABLE dbo.invoices ADD company_profile_id int NULL;
    IF COL_LENGTH(N'dbo.invoices', N'seller_name') IS NULL
        ALTER TABLE dbo.invoices ADD seller_name nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'seller_address') IS NULL
        ALTER TABLE dbo.invoices ADD seller_address nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'seller_state') IS NULL
        ALTER TABLE dbo.invoices ADD seller_state nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'seller_state_code') IS NULL
        ALTER TABLE dbo.invoices ADD seller_state_code nvarchar(10) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'seller_gstn') IS NULL
        ALTER TABLE dbo.invoices ADD seller_gstn nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'buyer_name') IS NULL
        ALTER TABLE dbo.invoices ADD buyer_name nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'buyer_address') IS NULL
        ALTER TABLE dbo.invoices ADD buyer_address nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'buyer_state') IS NULL
        ALTER TABLE dbo.invoices ADD buyer_state nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'buyer_state_code') IS NULL
        ALTER TABLE dbo.invoices ADD buyer_state_code nvarchar(10) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'buyer_gstn') IS NULL
        ALTER TABLE dbo.invoices ADD buyer_gstn nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'ship_to_address') IS NULL
        ALTER TABLE dbo.invoices ADD ship_to_address nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'gst_rate_id') IS NULL
        ALTER TABLE dbo.invoices ADD gst_rate_id int NULL;
END";
    invoiceSchemaCommand.ExecuteNonQuery();
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

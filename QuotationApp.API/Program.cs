using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using System.Text;
using System.IO;
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
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
builder.Services.AddSingleton<PasswordResetService>();

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

    using var quotationTimeEstimateCommand = connection.CreateCommand();
    quotationTimeEstimateCommand.CommandText = @"
BEGIN TRY
    BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.QuotationTimeEstimates', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QuotationTimeEstimates')
          AND name = N'IX_QuotationTimeEstimates_QuotationId_StageKey'
    )
        DROP INDEX IX_QuotationTimeEstimates_QuotationId_StageKey ON dbo.QuotationTimeEstimates;

    IF COL_LENGTH(N'dbo.QuotationTimeEstimates', N'ModuleName') IS NULL
    BEGIN
        ALTER TABLE dbo.QuotationTimeEstimates ADD ModuleName nvarchar(200) NULL;

        ;WITH FirstSelectedModule AS
        (
            SELECT QuotationId, MIN(ModuleName) AS ModuleName
            FROM dbo.QuotationModules
            GROUP BY QuotationId
        )
        UPDATE estimate
        SET ModuleName = moduleChoice.ModuleName
        FROM dbo.QuotationTimeEstimates estimate
        INNER JOIN FirstSelectedModule moduleChoice
            ON moduleChoice.QuotationId = estimate.QuotationId;

        INSERT INTO dbo.QuotationTimeEstimates (QuotationId, ModuleName, StageKey, StartWeek, EndWeek)
        SELECT estimate.QuotationId, quotationModule.ModuleName, estimate.StageKey, estimate.StartWeek, estimate.EndWeek
        FROM dbo.QuotationTimeEstimates estimate
        INNER JOIN dbo.QuotationModules quotationModule
            ON quotationModule.QuotationId = estimate.QuotationId
        WHERE quotationModule.ModuleName <> estimate.ModuleName;

        IF EXISTS (SELECT 1 FROM dbo.QuotationTimeEstimates WHERE ModuleName IS NULL)
        BEGIN
            RAISERROR(N'Cannot migrate quotation time estimates without a selected module.', 16, 1);
        END

        ALTER TABLE dbo.QuotationTimeEstimates ALTER COLUMN ModuleName nvarchar(200) NOT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QuotationTimeEstimates')
          AND name = N'IX_QuotationTimeEstimates_QuotationId_ModuleName_StageKey'
    )
        CREATE UNIQUE INDEX IX_QuotationTimeEstimates_QuotationId_ModuleName_StageKey
            ON dbo.QuotationTimeEstimates (QuotationId, ModuleName, StageKey);
END";
    quotationTimeEstimateCommand.CommandText += @"

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH";
    quotationTimeEstimateCommand.ExecuteNonQuery();

    using var purchaseOrderDiscountColumnsCommand = connection.CreateCommand();
    purchaseOrderDiscountColumnsCommand.CommandText = @"
IF OBJECT_ID(N'dbo.po_items', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.po_items', N'discount_percentage') IS NULL
        ALTER TABLE dbo.po_items ADD discount_percentage decimal(5,2) NOT NULL CONSTRAINT DF_po_items_discount_percentage DEFAULT 0;
    IF COL_LENGTH(N'dbo.po_items', N'discount_amount') IS NULL
        ALTER TABLE dbo.po_items ADD discount_amount decimal(12,2) NOT NULL CONSTRAINT DF_po_items_discount_amount DEFAULT 0;
END";
    purchaseOrderDiscountColumnsCommand.ExecuteNonQuery();

    using var invoiceDiscountColumnsCommand = connection.CreateCommand();
    invoiceDiscountColumnsCommand.CommandText = @"
IF OBJECT_ID(N'dbo.invoice_items', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.invoice_items', N'DiscountPercentage') IS NULL
        ALTER TABLE dbo.invoice_items ADD DiscountPercentage decimal(5,2) NOT NULL CONSTRAINT DF_invoice_items_discount_percentage DEFAULT 0;
    IF COL_LENGTH(N'dbo.invoice_items', N'DiscountAmount') IS NULL
        ALTER TABLE dbo.invoice_items ADD DiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_invoice_items_discount_amount DEFAULT 0;
END";
    invoiceDiscountColumnsCommand.ExecuteNonQuery();

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

    using var additionalScopesTableCommand = connection.CreateCommand();
    additionalScopesTableCommand.CommandText = @"
IF OBJECT_ID(N'dbo.AdditionalScopes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdditionalScopes
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdditionalScopes PRIMARY KEY,
        QuotationId nvarchar(50) NOT NULL,
        Requirement nvarchar(500) NOT NULL,
        ModulesId int NULL,
        Modules nvarchar(200) NOT NULL,
        NoOfManpower int NOT NULL,
        NoOfDays int NOT NULL,
        Rate decimal(18,2) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        Price decimal(18,2) NOT NULL,
        CONSTRAINT FK_AdditionalScopes_Modules FOREIGN KEY (ModulesId) REFERENCES dbo.Modules(Id),
        CONSTRAINT FK_AdditionalScopes_Quotations FOREIGN KEY (QuotationId) REFERENCES dbo.Quotations(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AdditionalScopes_ModulesId ON dbo.AdditionalScopes (ModulesId);
    CREATE INDEX IX_AdditionalScopes_QuotationId ON dbo.AdditionalScopes (QuotationId);
END";
    additionalScopesTableCommand.ExecuteNonQuery();

    using var additionalScopesModuleIdCommand = connection.CreateCommand();
    additionalScopesModuleIdCommand.CommandText = @"
IF OBJECT_ID(N'dbo.AdditionalScopes', N'U') IS NOT NULL
AND EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.AdditionalScopes')
      AND name = N'ModulesId'
      AND is_nullable = 0
)
    ALTER TABLE dbo.AdditionalScopes ALTER COLUMN ModulesId int NULL";
    additionalScopesModuleIdCommand.ExecuteNonQuery();

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

    using var invoiceSubscriptionSchemaCommand = connection.CreateCommand();
    invoiceSubscriptionSchemaCommand.CommandText = @"
IF COL_LENGTH(N'dbo.invoice_items', N'module_id') IS NULL
BEGIN
    ALTER TABLE dbo.invoice_items ADD module_id INT NULL;
    ALTER TABLE dbo.invoice_items
        ADD CONSTRAINT FK_invoice_items_modules
        FOREIGN KEY (module_id) REFERENCES dbo.Modules(Id);
END;
IF COL_LENGTH(N'dbo.CustomerModuleSubscription', N'InvoiceId') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerModuleSubscription ADD InvoiceId INT NULL;
    ALTER TABLE dbo.CustomerModuleSubscription
        ADD CONSTRAINT FK_CustomerModuleSubscription_Invoices
        FOREIGN KEY (InvoiceId) REFERENCES dbo.invoices(id);
    CREATE INDEX IX_CustomerModuleSubscription_InvoiceId
        ON dbo.CustomerModuleSubscription(InvoiceId);
END;
";
    invoiceSubscriptionSchemaCommand.ExecuteNonQuery();

    using var invoiceModuleBackfillCommand = connection.CreateCommand();
    invoiceModuleBackfillCommand.CommandText = @"
IF OBJECT_ID(N'dbo.invoice_items', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.invoice_items', N'module_id') IS NOT NULL
BEGIN
    UPDATE items
    SET module_id = modules.Id
    FROM dbo.invoice_items items
    INNER JOIN dbo.Modules modules
        ON LTRIM(RTRIM(items.description)) = LTRIM(RTRIM(modules.ModuleName))
    WHERE items.module_id IS NULL;
END;";
    invoiceModuleBackfillCommand.ExecuteNonQuery();

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

-- New PO verification fields
IF COL_LENGTH(N'dbo.purchase_orders', N'client_po_number') IS NULL
    ALTER TABLE dbo.purchase_orders ADD client_po_number nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'client_po_date') IS NULL
    ALTER TABLE dbo.purchase_orders ADD client_po_date date NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'client_po_amount') IS NULL
    ALTER TABLE dbo.purchase_orders ADD client_po_amount decimal(18,2) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'client_po_items') IS NULL
    ALTER TABLE dbo.purchase_orders ADD client_po_items nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'client_po_additional_scopes') IS NULL
    ALTER TABLE dbo.purchase_orders ADD client_po_additional_scopes nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'client_po_terms') IS NULL
    ALTER TABLE dbo.purchase_orders ADD client_po_terms nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'uploaded_file_path') IS NULL
    ALTER TABLE dbo.purchase_orders ADD uploaded_file_path nvarchar(500) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'uploaded_file_name') IS NULL
    ALTER TABLE dbo.purchase_orders ADD uploaded_file_name nvarchar(255) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'file_content_type') IS NULL
    ALTER TABLE dbo.purchase_orders ADD file_content_type nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.purchase_orders', N'created_by') IS NULL
    ALTER TABLE dbo.purchase_orders ADD created_by int NULL;

-- Update default for verification_status
IF COL_LENGTH(N'dbo.purchase_orders', N'verification_status') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.purchase_orders') AND col_name(parent_object_id, parent_column_id) = 'verification_status')
    BEGIN
        ALTER TABLE dbo.purchase_orders ADD CONSTRAINT DF_purchase_orders_verification_status DEFAULT 'Draft' FOR verification_status;
    END
END

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

    // Create PoAuditLog table if not exists
    using var poAuditLogCommand = connection.CreateCommand();
    poAuditLogCommand.CommandText = @"
IF OBJECT_ID(N'dbo.PoAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PoAuditLog
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PoAuditLog PRIMARY KEY,
        PoId int NOT NULL,
        Action nvarchar(50) NOT NULL,
        ChangedBy int NOT NULL,
        ChangedAt datetime2 NOT NULL,
        Notes nvarchar(max) NULL,
        CONSTRAINT FK_PoAuditLog_purchase_orders FOREIGN KEY (PoId) REFERENCES dbo.purchase_orders(id) ON DELETE CASCADE
    );
    CREATE INDEX IX_PoAuditLog_PoId ON dbo.PoAuditLog(PoId);
END
ELSE
BEGIN
    -- Add missing columns if they don't exist (for existing tables with different schema)
    IF COL_LENGTH(N'dbo.PoAuditLog', N'PoId') IS NULL
        ALTER TABLE dbo.PoAuditLog ADD PoId int NOT NULL DEFAULT 0;
    IF COL_LENGTH(N'dbo.PoAuditLog', N'ChangedBy') IS NULL
        ALTER TABLE dbo.PoAuditLog ADD ChangedBy int NOT NULL DEFAULT 0;
    IF COL_LENGTH(N'dbo.PoAuditLog', N'ChangedAt') IS NULL
        ALTER TABLE dbo.PoAuditLog ADD ChangedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME();
    IF COL_LENGTH(N'dbo.PoAuditLog', N'Action') IS NULL
        ALTER TABLE dbo.PoAuditLog ADD Action nvarchar(50) NOT NULL DEFAULT '';
    IF COL_LENGTH(N'dbo.PoAuditLog', N'Notes') IS NULL
        ALTER TABLE dbo.PoAuditLog ADD Notes nvarchar(max) NULL;
    -- Drop old columns if they exist with different naming
    IF COL_LENGTH(N'dbo.PoAuditLog', N'po_id') IS NOT NULL
        ALTER TABLE dbo.PoAuditLog DROP COLUMN po_id;
    IF COL_LENGTH(N'dbo.PoAuditLog', N'changed_by') IS NOT NULL
        ALTER TABLE dbo.PoAuditLog DROP COLUMN changed_by;
    IF COL_LENGTH(N'dbo.PoAuditLog', N'changed_at') IS NOT NULL
        ALTER TABLE dbo.PoAuditLog DROP COLUMN changed_at;
    -- Ensure index exists
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PoAuditLog_PoId' AND object_id = OBJECT_ID('dbo.PoAuditLog'))
        CREATE INDEX IX_PoAuditLog_PoId ON dbo.PoAuditLog(PoId);
END;
";
    poAuditLogCommand.ExecuteNonQuery();

    // Create unique index on (customer_id, client_po_number) ignoring NULLs
    using var uniqueIndexCommand = connection.CreateCommand();
    uniqueIndexCommand.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_purchase_orders_Customer_ClientPoNumber')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_purchase_orders_Customer_ClientPoNumber
    ON purchase_orders (customer_id, client_po_number)
    WHERE client_po_number IS NOT NULL;
END";
    uniqueIndexCommand.ExecuteNonQuery();

    // Update CHECK constraint on QuotationModules.ImplementationEffortUnit
    using var quotationModuleConstraintCommand = connection.CreateCommand();
    quotationModuleConstraintCommand.CommandText = @"
IF OBJECT_ID(N'dbo.QuotationModules', N'U') IS NOT NULL
BEGIN
    -- Drop existing CHECK constraint if exists
    DECLARE @constraintName NVARCHAR(128);
    DECLARE @sql NVARCHAR(MAX);
    SELECT @constraintName = name
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.QuotationModules')
      AND name = 'CK_QuotationModules_ImplementationEffortUnit';
    
    IF @constraintName IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.QuotationModules DROP CONSTRAINT ' + QUOTENAME(@constraintName);
        EXEC sp_executesql @sql;
    END

    -- Add new CHECK constraint with allowed values
    ALTER TABLE dbo.QuotationModules
    ADD CONSTRAINT CK_QuotationModules_ImplementationEffortUnit
    CHECK (
        ImplementationEffortUnit IS NULL
        OR ImplementationEffortUnit IN (
            N'1 Man Month',
            N'0.5 Man Month',
            N'2 Man Month',
            N'1 Day',
            N'2 Days',
            N'1 Week'
        )
        OR ImplementationEffortUnit LIKE N'% Days'
    );
END";
    quotationModuleConstraintCommand.ExecuteNonQuery();

    using var purchaseOrderItemSchemaCommand = connection.CreateCommand();
    purchaseOrderItemSchemaCommand.CommandText = @"
IF OBJECT_ID(N'dbo.po_items', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.po_items', N'module_price') IS NULL
        ALTER TABLE dbo.po_items ADD module_price decimal(12,2) NOT NULL CONSTRAINT DF_po_items_module_price DEFAULT 0;
    IF COL_LENGTH(N'dbo.po_items', N'implementation_price') IS NULL
        ALTER TABLE dbo.po_items ADD implementation_price decimal(12,2) NOT NULL CONSTRAINT DF_po_items_implementation_price DEFAULT 0;
END";
    purchaseOrderItemSchemaCommand.ExecuteNonQuery();

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
    IF COL_LENGTH(N'dbo.Modules', N'ImplementationEffortManDays') IS NULL
        ALTER TABLE dbo.Modules ADD ImplementationEffortManDays int NULL;
    IF COL_LENGTH(N'dbo.Modules', N'NoOfUsersForSingleInstallation') IS NULL
        ALTER TABLE dbo.Modules ADD NoOfUsersForSingleInstallation int NULL;
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
    IF COL_LENGTH(N'dbo.invoices', N'terms_of_sale') IS NULL
        ALTER TABLE dbo.invoices ADD terms_of_sale nvarchar(max) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'time_of_issue') IS NULL
        ALTER TABLE dbo.invoices ADD time_of_issue nvarchar(10) NULL;
    IF COL_LENGTH(N'dbo.invoices', N'additional_scopes_json') IS NULL
        ALTER TABLE dbo.invoices ADD additional_scopes_json nvarchar(max) NULL;
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

// Create uploads directory if it doesn't exist
var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// Serve uploaded files
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

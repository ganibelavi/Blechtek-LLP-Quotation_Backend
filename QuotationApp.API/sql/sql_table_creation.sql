-- Quotation application schema for SQL Server.
-- This script mirrors the entities and table mappings in
-- Data/QuotationDbContext.cs. Run it against a new database.

CREATE TABLE Users (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Email           NVARCHAR(200) NOT NULL,
    PasswordHash    NVARCHAR(500) NOT NULL,
    FirstName       NVARCHAR(100) NOT NULL,
    LastName        NVARCHAR(100) NOT NULL,
    IsActive        BIT NOT NULL,
    CreatedAt       DATETIME2 NOT NULL,
    LastLoginAt     DATETIME2 NULL,
    Role            NVARCHAR(50) NOT NULL,
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);

CREATE TABLE LoginHistory (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoginHistory PRIMARY KEY,
    Email           NVARCHAR(200) NOT NULL,
    LoggedAt        DATETIME2 NOT NULL,
    RemoteAddress   NVARCHAR(MAX) NULL
);

CREATE TABLE Modules (
    Id                          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Modules PRIMARY KEY,
    Pillar                      NVARCHAR(100) NOT NULL,
    ModuleName                  NVARCHAR(200) NOT NULL,
    Price                       DECIMAL(18,2) NULL,
    HsnCode                     NVARCHAR(20) NULL,
    SacCode                     NVARCHAR(20) NULL,
    ReverseChargeDefault        BIT NOT NULL,
    ImplementationEffortCost    DECIMAL(18,2) NULL,
    CONSTRAINT UQ_Modules_ModuleName UNIQUE (ModuleName)
);

CREATE TABLE Quotations (
    Id                  NVARCHAR(50) NOT NULL CONSTRAINT PK_Quotations PRIMARY KEY,
    OrganizationName    NVARCHAR(200) NOT NULL,
    ValidationDate      DATETIME2 NOT NULL,
    QuotationNo         NVARCHAR(50) NULL,
    Date                DATETIME2 NULL,
    ReferenceBy         NVARCHAR(150) NULL,
    CreatedByUser       NVARCHAR(200) NULL,
    QuotationToName     NVARCHAR(150) NOT NULL,
    QuotationToAddress  NVARCHAR(400) NOT NULL,
    QuotationToContactNo NVARCHAR(30) NOT NULL,
    QuotationToEmail    NVARCHAR(150) NOT NULL,
    GeneratedAt         DATETIME2 NOT NULL,
    DiscountPercentage  DECIMAL(5,2) NULL,
    ModulePriceTotal DECIMAL(18,2) NULL,
    ImplementationPriceTotal DECIMAL(18,2) NULL,
    Subtotal DECIMAL(18,2) NULL,
    DiscountAmount DECIMAL(18,2) NULL,
    FinalPrice DECIMAL(18,2) NULL
);

CREATE TABLE QuotationModules (
    QuotationId                NVARCHAR(50) NOT NULL,
    ModuleName                 NVARCHAR(200) NOT NULL,
    NoOfUsers                  INT NULL,
    NoOfInstallations          INT NULL,
    NoOfSites                  INT NULL,
    ImplementationEffortUnit   NVARCHAR(30) NULL,

    ModulePrice                DECIMAL(18,2) NULL,
    ImplementationUnitPrice    DECIMAL(18,2) NULL,
    ImplementationMultiplier   DECIMAL(10,2) NULL,
    ImplementationPrice        DECIMAL(18,2) NULL,
    ModuleSubtotal             DECIMAL(18,2) NULL,
    DiscountPercentage         DECIMAL(5,2) NULL,
    DiscountAmount             DECIMAL(18,2) NULL,
    FinalPrice                 DECIMAL(18,2) NULL,

    CONSTRAINT PK_QuotationModules
        PRIMARY KEY (QuotationId, ModuleName),

    CONSTRAINT FK_QuotationModules_Quotations
        FOREIGN KEY (QuotationId)
        REFERENCES Quotations(Id)
        ON DELETE CASCADE
);

CREATE INDEX IX_QuotationModules_ModuleName
    ON QuotationModules(ModuleName);

CREATE TABLE QuotationHistory (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuotationHistory PRIMARY KEY,
    QuotationId         NVARCHAR(50) NOT NULL,
    OrganizationName    NVARCHAR(200) NOT NULL,
    QuotationNo         NVARCHAR(50) NULL,
    Date                DATETIME2 NULL,
    ValidationDate      DATETIME2 NOT NULL,
    ReferenceBy         NVARCHAR(150) NULL,
    QuotationToName     NVARCHAR(150) NOT NULL,
    QuotationToAddress  NVARCHAR(400) NOT NULL,
    QuotationToContactNo NVARCHAR(30) NOT NULL,
    QuotationToEmail    NVARCHAR(150) NOT NULL,
    ModulesJson         NVARCHAR(MAX) NOT NULL,
    DiscountPercentage  DECIMAL(5,2) NULL,
    ChangedAt           DATETIME2 NOT NULL,
    ChangeType          NVARCHAR(30) NOT NULL
);

CREATE INDEX IX_QuotationHistory_QuotationId
    ON QuotationHistory(QuotationId);
-- ModulesJson is NVARCHAR(MAX), which SQL Server cannot index directly.
CREATE INDEX IX_QuotationHistory_OrganizationName
    ON QuotationHistory(OrganizationName);

CREATE TABLE customers (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_customers PRIMARY KEY,
    name            NVARCHAR(255) NOT NULL,
    address         NVARCHAR(1000) NULL,
    state           NVARCHAR(100) NULL,
    state_code      NVARCHAR(10) NULL,
    gstn            NVARCHAR(20) NULL,
    contact_name    NVARCHAR(150) NULL,
    contact_number  NVARCHAR(30) NULL,
    email           NVARCHAR(255) NULL,
    created_at      DATETIME2 NOT NULL
);

CREATE TABLE suppliers (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_suppliers PRIMARY KEY,
    name            NVARCHAR(255) NOT NULL,
    address         NVARCHAR(1000) NULL,
    state           NVARCHAR(100) NULL,
    state_code      NVARCHAR(10) NULL,
    gstn            NVARCHAR(20) NULL,
    created_at      DATETIME2 NOT NULL
);

CREATE TABLE company_profile (
    id                      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_company_profile PRIMARY KEY,
    name                    NVARCHAR(MAX) NOT NULL,
    address                 NVARCHAR(MAX) NULL,
    state                   NVARCHAR(MAX) NULL,
    state_code              NVARCHAR(MAX) NULL,
    gstn                    NVARCHAR(MAX) NULL,
    default_terms_of_sale   NVARCHAR(MAX) NULL,
    is_active               BIT NOT NULL
);

CREATE TABLE company_bank_accounts (
    id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_company_bank_accounts PRIMARY KEY,
    bank_name     NVARCHAR(MAX) NULL,
    account_no    NVARCHAR(MAX) NULL,
    account_type  NVARCHAR(MAX) NOT NULL,
    ifsc          NVARCHAR(MAX) NULL,
    msme_no       NVARCHAR(MAX) NULL,
    is_default    BIT NOT NULL,
    is_active     BIT NOT NULL
);

CREATE TABLE gst_rates (
    id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_gst_rates PRIMARY KEY,
    label        NVARCHAR(MAX) NOT NULL,
    sgst_pct     DECIMAL(18,2) NOT NULL,
    cgst_pct     DECIMAL(18,2) NOT NULL,
    igst_pct     DECIMAL(18,2) NOT NULL,
    is_active    BIT NOT NULL,
    created_at   DATETIME2 NOT NULL
);

CREATE TABLE terms_templates (
    id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_terms_templates PRIMARY KEY,
    type         NVARCHAR(MAX) NOT NULL,
    label        NVARCHAR(MAX) NOT NULL,
    content      NVARCHAR(MAX) NOT NULL,
    is_default   BIT NOT NULL,
    is_active    BIT NOT NULL,
    created_at   DATETIME2 NOT NULL
);

CREATE TABLE products (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_products PRIMARY KEY,
    name            NVARCHAR(255) NOT NULL,
    hsn_sac         NVARCHAR(20) NULL,
    uom             NVARCHAR(20) NOT NULL,
    default_rate    DECIMAL(12,2) NOT NULL,
    created_at      DATETIME2 NOT NULL
);

CREATE TABLE purchase_orders (
    id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_purchase_orders PRIMARY KEY,
    customer_id         INT NOT NULL,
    supplier_id         INT NULL,
    quotation_id        NVARCHAR(50) NULL,
    quotation_ref_no    NVARCHAR(100) NULL,
    quotation_ref_date  DATETIME2 NULL,
    po_no               NVARCHAR(50) NOT NULL,
    po_date             DATETIME2 NOT NULL,
    status              NVARCHAR(20) NOT NULL,
    delivery_terms      NVARCHAR(MAX) NULL,
    payment_terms       NVARCHAR(MAX) NULL,
    created_at          DATETIME2 NOT NULL,
    po_direction        NVARCHAR(20) NULL,
    received_from_email NVARCHAR(255) NULL,
    attachment_url      NVARCHAR(1000) NULL,
    verification_status NVARCHAR(30) NULL,
    verified_by         NVARCHAR(200) NULL,
    verified_at         DATETIME2 NULL,
    verification_notes  NVARCHAR(MAX) NULL,
    uploaded_by         NVARCHAR(200) NULL,
    received_at         DATETIME2 NULL,
    CONSTRAINT UQ_purchase_orders_PoNo UNIQUE (po_no),
    CONSTRAINT FK_purchase_orders_customers
        FOREIGN KEY (customer_id) REFERENCES customers(id),
    CONSTRAINT FK_purchase_orders_suppliers
        FOREIGN KEY (supplier_id) REFERENCES suppliers(id),
    CONSTRAINT FK_purchase_orders_quotations
        FOREIGN KEY (quotation_id) REFERENCES Quotations(Id)
);

CREATE INDEX IX_purchase_orders_customer_id ON purchase_orders(customer_id);
CREATE INDEX IX_purchase_orders_supplier_id ON purchase_orders(supplier_id);
CREATE INDEX IX_purchase_orders_quotation_id ON purchase_orders(quotation_id);

CREATE TABLE po_items (
    id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_po_items PRIMARY KEY,
    po_id       INT NOT NULL,
    product_id  INT NULL,
    description NVARCHAR(MAX) NOT NULL,
    qty         DECIMAL(12,2) NOT NULL,
    uom         NVARCHAR(20) NOT NULL,
    rate        DECIMAL(12,2) NOT NULL,
    CONSTRAINT FK_po_items_purchase_orders
        FOREIGN KEY (po_id) REFERENCES purchase_orders(id) ON DELETE CASCADE,
    CONSTRAINT FK_po_items_products
        FOREIGN KEY (product_id) REFERENCES products(id)
);

CREATE INDEX IX_po_items_po_id ON po_items(po_id);

CREATE TABLE po_status_history (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_po_status_history PRIMARY KEY,
    po_id           INT NOT NULL,
    from_status     NVARCHAR(20) NULL,
    to_status       NVARCHAR(20) NOT NULL,
    changed_by      NVARCHAR(200) NULL,
    changed_at      DATETIME2 NOT NULL,
    notes           NVARCHAR(MAX) NULL,
    CONSTRAINT FK_po_status_history_purchase_orders
        FOREIGN KEY (po_id) REFERENCES purchase_orders(id) ON DELETE CASCADE
);

CREATE INDEX IX_po_status_history_po_id ON po_status_history(po_id);

CREATE TABLE invoices (
    id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_invoices PRIMARY KEY,
    customer_id         INT NOT NULL,
    po_id               INT NULL,
    quotation_id        NVARCHAR(50) NULL,
    invoice_no          NVARCHAR(50) NOT NULL,
    invoice_date        DATETIME2 NOT NULL,
    place_of_supply     NVARCHAR(100) NULL,
    hsn_code            NVARCHAR(20) NULL,
    sac_code            NVARCHAR(20) NULL,
    sgst_pct            DECIMAL(5,2) NOT NULL,
    cgst_pct            DECIMAL(5,2) NOT NULL,
    igst_pct            DECIMAL(5,2) NOT NULL,
    tds_pct             DECIMAL(5,2) NOT NULL,
    insurance           DECIMAL(12,2) NOT NULL,
    reverse_charge      BIT NOT NULL,
    subtotal            DECIMAL(14,2) NOT NULL,
    grand_total         DECIMAL(14,2) NOT NULL,
    status              NVARCHAR(20) NOT NULL,
    amount_in_words     NVARCHAR(1000) NULL,
    created_at          DATETIME2 NOT NULL,
    company_profile_id  INT NULL,
    seller_name         NVARCHAR(255) NULL,
    seller_address      NVARCHAR(1000) NULL,
    seller_state        NVARCHAR(100) NULL,
    seller_state_code   NVARCHAR(10) NULL,
    seller_gstn         NVARCHAR(20) NULL,
    buyer_name          NVARCHAR(255) NULL,
    buyer_address       NVARCHAR(1000) NULL,
    buyer_state         NVARCHAR(100) NULL,
    buyer_state_code    NVARCHAR(10) NULL,
    buyer_gstn          NVARCHAR(20) NULL,
    ship_to_address     NVARCHAR(1000) NULL,
    gst_rate_id         INT NULL,
    CONSTRAINT UQ_invoices_InvoiceNo UNIQUE (invoice_no),
    CONSTRAINT FK_invoices_customers
        FOREIGN KEY (customer_id) REFERENCES customers(id)
    ,
    CONSTRAINT FK_invoices_quotations
        FOREIGN KEY (quotation_id) REFERENCES Quotations(Id)
);

CREATE INDEX IX_invoices_customer_id ON invoices(customer_id);
CREATE INDEX IX_invoices_po_id ON invoices(po_id);
CREATE INDEX IX_invoices_quotation_id ON invoices(quotation_id);

CREATE TABLE invoice_items (
    id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_invoice_items PRIMARY KEY,
    invoice_id  INT NOT NULL,
    product_id  INT NULL,
    description NVARCHAR(MAX) NOT NULL,
    qty         DECIMAL(12,2) NOT NULL,
    uom         NVARCHAR(20) NOT NULL,
    rate        DECIMAL(12,2) NOT NULL,
    CONSTRAINT FK_invoice_items_invoices
        FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE,
    CONSTRAINT FK_invoice_items_products
        FOREIGN KEY (product_id) REFERENCES products(id)
);

CREATE INDEX IX_invoice_items_invoice_id ON invoice_items(invoice_id);

CREATE TABLE invoice_bank_details (
    id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_invoice_bank_details PRIMARY KEY,
    invoice_id   INT NOT NULL,
    bank_name    NVARCHAR(255) NULL,
    account_no   NVARCHAR(100) NULL,
    account_type NVARCHAR(100) NULL,
    ifsc         NVARCHAR(50) NULL,
    msme_no      NVARCHAR(100) NULL,
    created_at   DATETIME2 NOT NULL,
    CONSTRAINT UQ_invoice_bank_details_invoice_id UNIQUE (invoice_id),
    CONSTRAINT FK_invoice_bank_details_invoices
        FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE
);

-- Module pricing history used to calculate initial prices and renewals.
CREATE TABLE ModulePricing (
    Id                          INT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_ModulePricing PRIMARY KEY,
    ModuleId                    INT NOT NULL,
    InitialPurchasePrice        DECIMAL(12,2) NOT NULL,
    RenewalPercentage           DECIMAL(5,2) NOT NULL,
    AnnualEscalationPercentage  DECIMAL(5,2) NOT NULL,
    PricingEffectiveFrom        DATE NOT NULL,
    PricingEffectiveTo          DATE NULL,
    IsActive                    BIT NOT NULL,
    CreatedAt                   DATETIME2 NOT NULL,
    CONSTRAINT FK_ModulePricing_Modules
        FOREIGN KEY (ModuleId) REFERENCES Modules(Id),
    CONSTRAINT CK_ModulePricing_Prices
        CHECK (InitialPurchasePrice >= 0),
    CONSTRAINT CK_ModulePricing_Percentages
        CHECK (
            RenewalPercentage >= 0
            AND AnnualEscalationPercentage >= 0
        ),
    CONSTRAINT CK_ModulePricing_EffectiveDates
        CHECK (
            PricingEffectiveTo IS NULL
            OR PricingEffectiveTo >= PricingEffectiveFrom
        )
);

CREATE INDEX IX_ModulePricing_ModuleId
    ON ModulePricing(ModuleId);
CREATE INDEX IX_ModulePricing_ModuleId_Active
    ON ModulePricing(ModuleId, IsActive);

-- A customer's active subscription to a module.
CREATE TABLE CustomerModuleSubscription (
    Id                          INT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_CustomerModuleSubscription PRIMARY KEY,
    CustomerId                  INT NOT NULL,
    ModuleId                    INT NOT NULL,
    QuotationId                 NVARCHAR(50) NULL,
    PurchaseDate                DATE NOT NULL,
    SubscriptionStartDate       DATE NOT NULL,
    SubscriptionEndDate         DATE NULL,
    CurrentYear                 INT NULL,
    InitialPurchasePrice        DECIMAL(12,2) NOT NULL,
    RenewalPercentage           DECIMAL(5,2) NOT NULL,
    AnnualEscalationPercentage  DECIMAL(5,2) NOT NULL,
    Status                      NVARCHAR(20) NOT NULL,
    NextRenewalDate             DATE NULL,
    CreatedAt                   DATETIME2 NOT NULL,
    CONSTRAINT FK_CustomerModuleSubscription_Customers
        FOREIGN KEY (CustomerId) REFERENCES customers(id),
    CONSTRAINT FK_CustomerModuleSubscription_Modules
        FOREIGN KEY (ModuleId) REFERENCES Modules(Id),
    CONSTRAINT FK_CustomerModuleSubscription_Quotations
        FOREIGN KEY (QuotationId) REFERENCES Quotations(Id),
    CONSTRAINT CK_CustomerModuleSubscription_Status
        CHECK (Status IN ('active', 'expired', 'cancelled', 'pending')),
    CONSTRAINT CK_CustomerModuleSubscription_Prices
        CHECK (
            InitialPurchasePrice >= 0
            AND RenewalPercentage >= 0
            AND AnnualEscalationPercentage >= 0
        ),
    CONSTRAINT CK_CustomerModuleSubscription_Dates
        CHECK (
            SubscriptionEndDate IS NULL
            OR SubscriptionEndDate >= SubscriptionStartDate
        )
);

CREATE INDEX IX_CustomerModuleSubscription_CustomerId
    ON CustomerModuleSubscription(CustomerId);
CREATE INDEX IX_CustomerModuleSubscription_ModuleId
    ON CustomerModuleSubscription(ModuleId);
CREATE INDEX IX_CustomerModuleSubscription_Status
    ON CustomerModuleSubscription(Status);
CREATE INDEX IX_CustomerModuleSubscription_NextRenewalDate
    ON CustomerModuleSubscription(NextRenewalDate);

-- One renewal record per subscription and renewal period.
CREATE TABLE SubscriptionRenewal (
    Id                    INT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_SubscriptionRenewal PRIMARY KEY,
    SubscriptionId        INT NOT NULL,
    RenewalYear           INT NOT NULL,
    PeriodStartDate       DATE NOT NULL,
    PeriodEndDate         DATE NOT NULL,
    PreviousAmount        DECIMAL(12,2) NOT NULL,
    EscalationPercentage  DECIMAL(5,2) NOT NULL,
    RenewalAmount         DECIMAL(12,2) NOT NULL,
    QuotationId           NVARCHAR(50) NULL,
    InvoiceId             INT NULL,
    Status                NVARCHAR(20) NOT NULL,
    CreatedAt             DATETIME2 NOT NULL,
    CONSTRAINT FK_SubscriptionRenewal_Subscriptions
        FOREIGN KEY (SubscriptionId)
        REFERENCES CustomerModuleSubscription(Id),
    CONSTRAINT FK_SubscriptionRenewal_Quotations
        FOREIGN KEY (QuotationId) REFERENCES Quotations(Id),
    CONSTRAINT FK_SubscriptionRenewal_Invoices
        FOREIGN KEY (InvoiceId) REFERENCES invoices(id),
    CONSTRAINT UQ_SubscriptionRenewal_Subscription_Year
        UNIQUE (SubscriptionId, RenewalYear),
    CONSTRAINT CK_SubscriptionRenewal_Status
        CHECK (Status IN ('pending', 'quoted', 'invoiced', 'paid', 'cancelled')),
    CONSTRAINT CK_SubscriptionRenewal_Amounts
        CHECK (
            PreviousAmount >= 0
            AND EscalationPercentage >= 0
            AND RenewalAmount >= 0
        ),
    CONSTRAINT CK_SubscriptionRenewal_Period
        CHECK (PeriodEndDate >= PeriodStartDate)
);

CREATE INDEX IX_SubscriptionRenewal_SubscriptionId
    ON SubscriptionRenewal(SubscriptionId);
CREATE INDEX IX_SubscriptionRenewal_Status
    ON SubscriptionRenewal(Status);
CREATE INDEX IX_SubscriptionRenewal_InvoiceId
    ON SubscriptionRenewal(InvoiceId);

-- The EF model declares these triggers on purchase_orders and invoices.
GO
CREATE TRIGGER trg_po_verification_history
ON purchase_orders
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO po_status_history
        (po_id, from_status, to_status, changed_by, changed_at, notes)
    SELECT
        i.id,
        d.verification_status,
        i.verification_status,
        i.verified_by,
        SYSUTCDATETIME(),
        i.verification_notes
    FROM inserted AS i
    INNER JOIN deleted AS d ON d.id = i.id
    WHERE ISNULL(i.verification_status, '') <> ISNULL(d.verification_status, '');
END;
GO

CREATE TRIGGER trg_invoice_requires_verified_po
ON invoices
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        INNER JOIN purchase_orders AS po ON po.id = i.po_id
        WHERE i.po_id IS NOT NULL
          AND ISNULL(po.verification_status, '') <> 'verified'
    )
    BEGIN
        RAISERROR(
            'Cannot create invoice: the referenced purchase order has not been verified.',
            16,
            1
        );
        ROLLBACK TRANSACTION;
    END;
END;
GO

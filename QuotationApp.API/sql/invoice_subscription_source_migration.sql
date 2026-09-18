-- Run this once against an existing database.
-- New databases are covered by sql_table_creation.sql.

IF COL_LENGTH(N'dbo.invoice_items', N'module_id') IS NULL
BEGIN
    ALTER TABLE dbo.invoice_items ADD module_id INT NULL;
    ALTER TABLE dbo.invoice_items
        ADD CONSTRAINT FK_invoice_items_modules
        FOREIGN KEY (module_id) REFERENCES dbo.Modules(Id);
END;
GO

IF COL_LENGTH(N'dbo.CustomerModuleSubscription', N'InvoiceId') IS NULL
BEGIN
    ALTER TABLE dbo.CustomerModuleSubscription ADD InvoiceId INT NULL;
    ALTER TABLE dbo.CustomerModuleSubscription
        ADD CONSTRAINT FK_CustomerModuleSubscription_Invoices
        FOREIGN KEY (InvoiceId) REFERENCES dbo.invoices(id);
    CREATE INDEX IX_CustomerModuleSubscription_InvoiceId
        ON dbo.CustomerModuleSubscription(InvoiceId);
END;
GO

-- Existing invoice rows retain their quotation relationship. New and updated
-- invoice rows receive module_id from the module name when they are saved.
-- Run the following statement separately after the ALTER TABLE statements.
IF OBJECT_ID(N'dbo.invoice_items', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.invoice_items', N'module_id') IS NOT NULL
BEGIN
    UPDATE items
    SET module_id = modules.Id
    FROM dbo.invoice_items items
    INNER JOIN dbo.Modules modules
        ON LTRIM(RTRIM(items.description)) = LTRIM(RTRIM(modules.ModuleName))
    WHERE items.module_id IS NULL;
END;
GO

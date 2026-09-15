-- Run this once against an existing database.
-- The main sql_table_creation.sql script contains the same table for new databases.

IF OBJECT_ID(N'dbo.SubscriptionPaymentHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SubscriptionPaymentHistory (
        Id                    INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_SubscriptionPaymentHistory PRIMARY KEY,
        SubscriptionId        INT NOT NULL,
        InvoiceId             INT NULL,
        PaymentDate           DATE NOT NULL,
        Amount                DECIMAL(12,2) NULL,
        Status                NVARCHAR(20) NOT NULL,
        PaymentMode           NVARCHAR(30) NULL,
        TransactionReference  NVARCHAR(100) NULL,
        Notes                 NVARCHAR(1000) NULL,
        CreatedAt             DATETIME2 NOT NULL,
        CONSTRAINT FK_SubscriptionPaymentHistory_Subscription
            FOREIGN KEY (SubscriptionId) REFERENCES dbo.CustomerModuleSubscription(Id),
        CONSTRAINT FK_SubscriptionPaymentHistory_Invoice
            FOREIGN KEY (InvoiceId) REFERENCES dbo.invoices(id),
        CONSTRAINT CK_SubscriptionPaymentHistory_Amount
            CHECK (Amount IS NULL OR Amount >= 0),
        CONSTRAINT CK_SubscriptionPaymentHistory_Status
            CHECK (Status IN ('paid', 'pending', 'overdue', 'refunded'))
    );

    CREATE INDEX IX_SubscriptionPaymentHistory_SubscriptionId
        ON dbo.SubscriptionPaymentHistory(SubscriptionId);
    CREATE INDEX IX_SubscriptionPaymentHistory_InvoiceId
        ON dbo.SubscriptionPaymentHistory(InvoiceId);
END;

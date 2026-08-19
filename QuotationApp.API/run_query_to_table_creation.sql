-- Quotation Application Database Migration Script
-- Run this script to create all tables and insert sample test user
-- Test User: test@test.com / Test@123

-- =============================================
-- Create Tables
-- =============================================

-- Modules Table
CREATE TABLE [Modules] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Pillar] NVARCHAR(100) NOT NULL,
    [ModuleName] NVARCHAR(200) NOT NULL,
    [Price] DECIMAL(18,2) NULL
);

CREATE UNIQUE INDEX [IX_Modules_ModuleName] ON [Modules]([ModuleName]);

-- Users Table
CREATE TABLE [Users] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Email] NVARCHAR(200) NOT NULL,
    [PasswordHash] NVARCHAR(500) NOT NULL,
    [FirstName] NVARCHAR(100) NULL,
    [LastName] NVARCHAR(100) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastLoginAt] DATETIME2 NULL,
    [Role] NVARCHAR(50) NULL DEFAULT 'User'
);

CREATE UNIQUE INDEX [IX_Users_Email] ON [Users]([Email]);

-- LoginHistory Table
CREATE TABLE [LoginHistory] (
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Email] NVARCHAR(200) NOT NULL,
    [LoggedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    [RemoteAddress] NVARCHAR(100) NULL
);

-- Quotations Table
CREATE TABLE [Quotations] (
    [Id] NVARCHAR(50) NOT NULL PRIMARY KEY,
    [OrganizationName] NVARCHAR(200) NOT NULL,
    [ValidationDate] DATETIME2 NOT NULL,
    [QuotationNo] NVARCHAR(50) NULL,
    [Date] DATETIME2 NULL,
    [QuotationToName] NVARCHAR(150) NOT NULL,
    [QuotationToAddress] NVARCHAR(400) NOT NULL,
    [QuotationToContactNo] NVARCHAR(30) NOT NULL,
    [QuotationToEmail] NVARCHAR(150) NOT NULL,
    [DiscountPercentage] DECIMAL(5, 2) NULL,
    [GeneratedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- QuotationModules Table (Junction Table)
CREATE TABLE [QuotationModules] (
    [QuotationId] NVARCHAR(50) NOT NULL,
    [ModuleName] NVARCHAR(200) NOT NULL,
    CONSTRAINT [PK_QuotationModules] PRIMARY KEY ([QuotationId], [ModuleName]),
    CONSTRAINT [FK_QuotationModules_Quotations] FOREIGN KEY ([QuotationId]) 
        REFERENCES [Quotations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_QuotationModules_Modules] FOREIGN KEY ([ModuleName]) 
        REFERENCES [Modules]([ModuleName]) ON DELETE NO ACTION
);

-- =============================================
-- Insert Sample Test User
-- =============================================
-- Password: Test@123 (BCrypt hashed with work factor 12)
-- Email: test@test.com
INSERT INTO [Users] ([Email], [PasswordHash], [FirstName], [LastName], [IsActive], [CreatedAt], [Role])
VALUES (
    'test@test.com',
    '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewdBPj/RK.PZvO.S',
    'Test',
    'User',
    1,
    SYSUTCDATETIME(),
    'Admin'
);

-- =============================================
-- Sample Modules Data (Optional)
-- =============================================
INSERT INTO [Modules] ([Pillar], [ModuleName], [Price]) VALUES
('Engineering', 'Structural Design', 5000.00),
('Engineering', 'Mechanical Design', 4500.00),
('Engineering', 'Electrical Design', 4000.00),
('Project Management', 'Project Planning', 3000.00),
('Project Management', 'Resource Allocation', 2500.00),
('Quality Assurance', 'Quality Control', 3500.00),
('Quality Assurance', 'Testing & Validation', 3000.00),
('Documentation', 'Technical Documentation', 2000.00),
('Documentation', 'User Manuals', 1500.00);

-- =============================================
-- Verification Queries
-- =============================================
-- Verify tables created
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME IN ('Modules', 'Users', 'LoginHistory', 'Quotations', 'QuotationModules');

-- Verify test user
SELECT Id, Email, FirstName, LastName, Role, IsActive, CreatedAt FROM [Users] WHERE Email = 'test@test.com';

-- Verify sample modules
SELECT * FROM [Modules];
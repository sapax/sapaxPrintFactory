using System.Collections.Generic;
using System.Linq;

namespace sapaXPrintMaster.Services
{
    public record TableTemplate(string Name, string[] DependsOn, bool AllowedBeforeLogin, string Sql, string? Upgrade = null);

    public static class SchemaTemplates
    {
        // Creation order = dependency order.
        // Serverless: no cloud API, no API keys, no activation keys.
        // Job sources (Manual, PowerAutomate, PowerApps, WebForm, ...) INSERT into dbo.PrintJobs.
        // Desktop agents poll that table.
        public static readonly IReadOnlyList<TableTemplate> All = new List<TableTemplate>
        {
            new("Users", new string[0], true, @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
CREATE TABLE dbo.Users (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    UserID       NVARCHAR(100) NOT NULL CONSTRAINT UQ_Users_UserID UNIQUE,
    PasswordHash VARBINARY(32) NOT NULL,
    Role         NVARCHAR(50)  NOT NULL,
    UserName     NVARCHAR(100) NULL,
    Email        NVARCHAR(255) NULL,
    Active       BIT NOT NULL CONSTRAINT DF_Users_Active DEFAULT 1
);"),

            new("Companies", new string[0], false, @"
IF OBJECT_ID(N'dbo.Companies', N'U') IS NULL
CREATE TABLE dbo.Companies (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Companies PRIMARY KEY,
    CompanyID   NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Companies_CompanyID UNIQUE,
    CompanyName NVARCHAR(200) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_Companies_Active DEFAULT 1,
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_Companies_Created DEFAULT SYSUTCDATETIME()
);"),

            new("SubAreas", new[] { "Companies" }, false, @"
IF OBJECT_ID(N'dbo.SubAreas', N'U') IS NULL
CREATE TABLE dbo.SubAreas (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SubAreas PRIMARY KEY,
    CompanyID   NVARCHAR(50)  NOT NULL CONSTRAINT FK_SubAreas_Company REFERENCES dbo.Companies(CompanyID),
    SubAreaID   NVARCHAR(50)  NOT NULL,
    SubAreaName NVARCHAR(200) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_SubAreas_Active DEFAULT 1,
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_SubAreas_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SubAreas UNIQUE (CompanyID, SubAreaID)
);"),

            new("Computers", new[] { "Companies", "SubAreas" }, false, @"
IF OBJECT_ID(N'dbo.Computers', N'U') IS NULL
CREATE TABLE dbo.Computers (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Computers PRIMARY KEY,
    ComputerID   NVARCHAR(100) NOT NULL CONSTRAINT UQ_Computers_ComputerID UNIQUE,
    ComputerName NVARCHAR(200) NULL,
    CompanyID    NVARCHAR(50)  NULL,
    SubAreaID    NVARCHAR(50)  NULL,
    Active       BIT NOT NULL CONSTRAINT DF_Computers_Active DEFAULT 1,
    LastSeenAt   DATETIME2 NULL,
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_Computers_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Computers_SubArea FOREIGN KEY (CompanyID, SubAreaID) REFERENCES dbo.SubAreas(CompanyID, SubAreaID)
);", @"
-- Removes columns and tables from older versions (API keys, activation keys).
IF OBJECT_ID(N'dbo.Computers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Computers', N'ApiKeyId') IS NOT NULL
BEGIN
    DECLARE @fk sysname = (SELECT TOP 1 fk.name FROM sys.foreign_keys fk
                           JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                           WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Computers')
                             AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'ApiKeyId');
    IF @fk IS NOT NULL EXEC(N'ALTER TABLE dbo.Computers DROP CONSTRAINT ' + @fk);
    ALTER TABLE dbo.Computers DROP COLUMN ApiKeyId;
END;

IF OBJECT_ID(N'dbo.Computers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Computers', N'ActivationKeyId') IS NOT NULL
BEGIN
    DECLARE @afk sysname = (SELECT TOP 1 fk.name FROM sys.foreign_keys fk
                            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                            WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Computers')
                              AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'ActivationKeyId');
    IF @afk IS NOT NULL EXEC(N'ALTER TABLE dbo.Computers DROP CONSTRAINT ' + @afk);
    ALTER TABLE dbo.Computers DROP COLUMN ActivationKeyId;
END;

IF OBJECT_ID(N'dbo.ActivationKeys', N'U') IS NOT NULL
    DROP TABLE dbo.ActivationKeys;"),

            new("Printers", new[] { "Computers" }, false, @"
IF OBJECT_ID(N'dbo.Printers', N'U') IS NULL
CREATE TABLE dbo.Printers (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Printers PRIMARY KEY,
    ComputerID  NVARCHAR(100) NOT NULL CONSTRAINT FK_Printers_Computer REFERENCES dbo.Computers(ComputerID),
    PrinterID   NVARCHAR(100) NOT NULL,
    PrinterName NVARCHAR(200) NULL,
    Status      NVARCHAR(50)  NULL,
    Active      BIT NOT NULL CONSTRAINT DF_Printers_Active DEFAULT 1,
    LastSeenAt  DATETIME2 NULL,
    CONSTRAINT UQ_Printers UNIQUE (ComputerID, PrinterID)
);", @"
IF OBJECT_ID(N'dbo.Printers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Printers', N'PrinterName') IS NULL ALTER TABLE dbo.Printers ADD PrinterName NVARCHAR(200) NULL;
    IF COL_LENGTH(N'dbo.Printers', N'Status')      IS NULL ALTER TABLE dbo.Printers ADD Status      NVARCHAR(50)  NULL;
END"),

            new("PrintJobs", new[] { "Computers" }, false, @"
IF OBJECT_ID(N'dbo.PrintJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrintJobs (
        JobId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PrintJobs PRIMARY KEY,
        JobType       NVARCHAR(10)   NOT NULL CONSTRAINT CK_PrintJobs_Type CHECK (JobType IN ('PDF','ZPL')),
        PrinterID     NVARCHAR(100)  NOT NULL,
        FileName      NVARCHAR(260)  NULL,
        Payload       NVARCHAR(MAX)  NULL,
        Source        NVARCHAR(50)   NULL,
        RequestedBy   NVARCHAR(100)  NULL,
        Status        NVARCHAR(20)   NOT NULL CONSTRAINT DF_PrintJobs_Status DEFAULT 'Queued',
        AttemptCount  INT            NOT NULL CONSTRAINT DF_PrintJobs_Attempts DEFAULT 0,
        ErrorMessage  NVARCHAR(1000) NULL,
        ComputerID    NVARCHAR(100)  NULL CONSTRAINT FK_PrintJobs_Computer REFERENCES dbo.Computers(ComputerID),
        PrinterName   NVARCHAR(200)  NULL,
        PrinterStatus NVARCHAR(50)   NULL,
        SpoolerJobId  INT            NULL,
        SpoolerStatus NVARCHAR(200)  NULL,
        LockedUntil   DATETIME2      NULL,
        CreatedAt     DATETIME2      NOT NULL CONSTRAINT DF_PrintJobs_Created DEFAULT SYSUTCDATETIME(),
        ClaimedAt     DATETIME2      NULL,
        CompletedAt   DATETIME2      NULL
    );
    CREATE INDEX IX_PrintJobs_Queue   ON dbo.PrintJobs (Status, PrinterID, CreatedAt);
    CREATE INDEX IX_PrintJobs_Printer ON dbo.PrintJobs (PrinterID, CreatedAt DESC);
END", @"
IF OBJECT_ID(N'dbo.PrintJobs', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'IX_PrintJobs_Poll')
        DROP INDEX IX_PrintJobs_Poll ON dbo.PrintJobs;

    IF COL_LENGTH(N'dbo.PrintJobs', N'Source')        IS NULL ALTER TABLE dbo.PrintJobs ADD Source        NVARCHAR(50)  NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'PrinterName')   IS NULL ALTER TABLE dbo.PrintJobs ADD PrinterName   NVARCHAR(200) NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'PrinterStatus') IS NULL ALTER TABLE dbo.PrintJobs ADD PrinterStatus NVARCHAR(50)  NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'SpoolerJobId')  IS NULL ALTER TABLE dbo.PrintJobs ADD SpoolerJobId  INT           NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'SpoolerStatus') IS NULL ALTER TABLE dbo.PrintJobs ADD SpoolerStatus NVARCHAR(200) NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'LockedUntil')   IS NULL ALTER TABLE dbo.PrintJobs ADD LockedUntil   DATETIME2     NULL;

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'ComputerID' AND is_nullable = 0)
        ALTER TABLE dbo.PrintJobs ALTER COLUMN ComputerID NVARCHAR(100) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'IX_PrintJobs_Queue')
        CREATE INDEX IX_PrintJobs_Queue ON dbo.PrintJobs (Status, PrinterID, CreatedAt);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'IX_PrintJobs_Printer')
        CREATE INDEX IX_PrintJobs_Printer ON dbo.PrintJobs (PrinterID, CreatedAt DESC);
END"),
        };

        public static TableTemplate Get(string name) => All.First(t => t.Name == name);

        public static IEnumerable<string> Available(bool loggedIn) =>
            All.Where(t => loggedIn || t.AllowedBeforeLogin).Select(t => t.Name);
    }
}
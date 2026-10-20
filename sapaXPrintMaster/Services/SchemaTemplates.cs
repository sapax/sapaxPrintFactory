using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace sapaXPrintMaster.Services
{
    public record TableTemplate(string Name, string[] DependsOn, bool AllowedBeforeLogin, string Sql, string? Upgrade = null);

    public static class SchemaTemplates
    {
        // Creation order = dependency order
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
            new("ActivationKeys", new string[0], false, @"
IF OBJECT_ID(N'dbo.ActivationKeys', N'U') IS NULL
CREATE TABLE dbo.ActivationKeys (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ActivationKeys PRIMARY KEY,
    ActivationKey  NVARCHAR(100) NOT NULL CONSTRAINT UQ_ActivationKeys_Key UNIQUE,
    Active         BIT NOT NULL CONSTRAINT DF_ActivationKeys_Active DEFAULT 1,
    Expiration     BIT NOT NULL CONSTRAINT DF_ActivationKeys_Exp DEFAULT 0,
    ExpirationDate DATETIME2 NULL,
    CreatedAt      DATETIME2 NOT NULL CONSTRAINT DF_ActivationKeys_Created DEFAULT SYSUTCDATETIME()
);"),
            new("ApiKeys", new string[0], false, @"
IF OBJECT_ID(N'dbo.ApiKeys', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiKeys (
        ApiKeyId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ApiKeys PRIMARY KEY,
        KeyPrefix NVARCHAR(20)  NOT NULL,
        KeyHash   VARBINARY(32) NOT NULL,
        OwnerType NVARCHAR(50)  NOT NULL,
        OwnerId   NVARCHAR(100) NOT NULL,
        IsActive  BIT NOT NULL CONSTRAINT DF_ApiKeys_Active DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_ApiKeys_Created DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ApiKeys_KeyHash ON dbo.ApiKeys (KeyHash) INCLUDE (IsActive, OwnerType, OwnerId);
END"),
            new("Companies", new string[0], false, @"
IF OBJECT_ID(N'dbo.Companies', N'U') IS NULL
CREATE TABLE dbo.Companies (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Companies PRIMARY KEY,
    CompanyID   NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Companies_CompanyID UNIQUE,
    CompanyName NVARCHAR(200) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_Companies_Active DEFAULT 1,
    CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_Companies_Created DEFAULT SYSUTCDATETIME()
);"),
            new("SubAreas", new[]{"Companies"}, false, @"
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
            new("Computers", new[]{"Companies","SubAreas","ApiKeys","ActivationKeys"}, false, @"
IF OBJECT_ID(N'dbo.Computers', N'U') IS NULL
CREATE TABLE dbo.Computers (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Computers PRIMARY KEY,
    ComputerID      NVARCHAR(100) NOT NULL CONSTRAINT UQ_Computers_ComputerID UNIQUE,
    ComputerName    NVARCHAR(200) NULL,
    CompanyID       NVARCHAR(50)  NULL,
    SubAreaID       NVARCHAR(50)  NULL,
    ApiKeyId        UNIQUEIDENTIFIER NULL CONSTRAINT FK_Computers_ApiKey REFERENCES dbo.ApiKeys(ApiKeyId),
    ActivationKeyId INT NULL CONSTRAINT FK_Computers_Activation REFERENCES dbo.ActivationKeys(Id),
    Active          BIT NOT NULL CONSTRAINT DF_Computers_Active DEFAULT 1,
    LastSeenAt      DATETIME2 NULL,
    CreatedAt       DATETIME2 NOT NULL CONSTRAINT DF_Computers_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Computers_SubArea FOREIGN KEY (CompanyID, SubAreaID) REFERENCES dbo.SubAreas(CompanyID, SubAreaID)
);"),
            new("Printers", new[]{"Computers"}, false, @"
IF OBJECT_ID(N'dbo.Printers', N'U') IS NULL
CREATE TABLE dbo.Printers (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Printers PRIMARY KEY,
    ComputerID NVARCHAR(100) NOT NULL CONSTRAINT FK_Printers_Computer REFERENCES dbo.Computers(ComputerID),
    PrinterID  NVARCHAR(100) NOT NULL,
    Active     BIT NOT NULL CONSTRAINT DF_Printers_Active DEFAULT 1,
    LastSeenAt DATETIME2 NULL,
    CONSTRAINT UQ_Printers UNIQUE (ComputerID, PrinterID)
);"),
            new("PrintJobs", new[]{"Computers"}, false, @"
IF OBJECT_ID(N'dbo.PrintJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrintJobs (
        JobId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PrintJobs PRIMARY KEY,
        JobType       NVARCHAR(10)   NOT NULL,                 -- 'PDF' or 'ZPL'
        ComputerID    NVARCHAR(100)  NULL CONSTRAINT FK_PrintJobs_Computer REFERENCES dbo.Computers(ComputerID), -- set when an agent claims the job
        AgentID       NVARCHAR(100)  NULL,                     -- X-Agent-Id exactly as the agent sent it
        PrinterID     NVARCHAR(100)  NOT NULL,
        PrinterName   NVARCHAR(200)  NULL,                     -- Windows printer name, reported by the agent
        FileName      NVARCHAR(260)  NULL,
        Payload       NVARCHAR(MAX)  NULL,                     -- ZPL text or PDF base64
        RequestedBy   NVARCHAR(100)  NULL,
        Status        NVARCHAR(20)   NOT NULL CONSTRAINT DF_PrintJobs_Status DEFAULT 'Queued', -- Queued, Claimed, Completed, Failed
        AttemptCount  INT            NOT NULL CONSTRAINT DF_PrintJobs_Attempts DEFAULT 0,
        ErrorMessage  NVARCHAR(1000) NULL,
        PrinterStatus NVARCHAR(50)   NULL,                     -- printer state when the agent sent the job (Online, Offline...)
        SpoolerJobId  INT            NULL,                     -- Windows spooler job number
        SpoolerStatus NVARCHAR(200)  NULL,                     -- last spooler status seen for the job
        LockedUntil   DATETIME2      NULL,
        CreatedAt     DATETIME2      NOT NULL CONSTRAINT DF_PrintJobs_Created DEFAULT SYSUTCDATETIME(),
        ClaimedAt     DATETIME2      NULL,
        CompletedAt   DATETIME2      NULL                      -- when the result (Completed / Failed) was confirmed
    );
    CREATE INDEX IX_PrintJobs_Poll    ON dbo.PrintJobs (ComputerID, Status, CreatedAt);
    CREATE INDEX IX_PrintJobs_Printer ON dbo.PrintJobs (PrinterID, CreatedAt DESC);
END", @"
IF OBJECT_ID(N'dbo.PrintJobs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.PrintJobs', N'AgentID')       IS NULL ALTER TABLE dbo.PrintJobs ADD AgentID       NVARCHAR(100) NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'PrinterName')   IS NULL ALTER TABLE dbo.PrintJobs ADD PrinterName   NVARCHAR(200) NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'PrinterStatus') IS NULL ALTER TABLE dbo.PrintJobs ADD PrinterStatus NVARCHAR(50)  NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'SpoolerJobId')  IS NULL ALTER TABLE dbo.PrintJobs ADD SpoolerJobId  INT           NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'SpoolerStatus') IS NULL ALTER TABLE dbo.PrintJobs ADD SpoolerStatus NVARCHAR(200) NULL;
    IF COL_LENGTH(N'dbo.PrintJobs', N'LockedUntil')   IS NULL ALTER TABLE dbo.PrintJobs ADD LockedUntil   DATETIME2     NULL;

    -- ComputerID is only known once an agent claims the job, so it must allow NULL
    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'ComputerID' AND is_nullable = 0)
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'IX_PrintJobs_Poll')
            DROP INDEX IX_PrintJobs_Poll ON dbo.PrintJobs;
        ALTER TABLE dbo.PrintJobs ALTER COLUMN ComputerID NVARCHAR(100) NULL;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'IX_PrintJobs_Poll')
        CREATE INDEX IX_PrintJobs_Poll ON dbo.PrintJobs (ComputerID, Status, CreatedAt);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PrintJobs') AND name = N'IX_PrintJobs_Printer')
        CREATE INDEX IX_PrintJobs_Printer ON dbo.PrintJobs (PrinterID, CreatedAt DESC);
END"),
            new("PrinterTemplate", new string[0], false, @"
IF OBJECT_ID(N'dbo.PrinterTemplate', N'U') IS NULL
CREATE TABLE dbo.PrinterTemplate (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrinterTemplate PRIMARY KEY,
    ComputerID       NVARCHAR(100) NOT NULL,
    PrinterID        NVARCHAR(100) NOT NULL,
    PlantID          NVARCHAR(50)  NOT NULL,
    PlantDescription NVARCHAR(255) NULL,
    Active           BIT NOT NULL CONSTRAINT DF_PrinterTemplate_Active DEFAULT 1,
    API              NVARCHAR(500) NULL,
    [Authorization]  NVARCHAR(MAX) NULL
);"),
        };

        public static TableTemplate Get(string name) => All.First(t => t.Name == name);

        public static IEnumerable<string> Available(bool loggedIn) =>
            All.Where(t => loggedIn || t.AllowedBeforeLogin).Select(t => t.Name);
    }
}
-- Run this script on database BMSSafariRwp if ManualRates table does not exist.
-- Usage: Execute in SSMS or via sqlcmd against your connection.

USE BMSSafariRwp;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ManualRates')
BEGIN
    CREATE TABLE ManualRates (
        SNo INT IDENTITY(1,1) PRIMARY KEY,
        CustomerNo NVARCHAR(50) NOT NULL,
        Phase NVARCHAR(100) NOT NULL,
        Size NVARCHAR(50) NULL,
        Category NVARCHAR(100) NOT NULL,
        UnitType NVARCHAR(50) NOT NULL,
        Misc INT NOT NULL DEFAULT 0,
        Tax INT NOT NULL DEFAULT 0,
        MaintCharges INT NOT NULL DEFAULT 0,
        Total AS (Misc + Tax + MaintCharges) PERSISTED
    );
    PRINT 'Table ManualRates created successfully.';
END
ELSE
    PRINT 'Table ManualRates already exists.';
GO

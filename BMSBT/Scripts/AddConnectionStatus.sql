-- Fix: SqlException: Invalid column name 'ConnectionStatus'
-- Run this on database BMSSafariRwp (see Connection in db.txt).
-- When ConnectionStatus = 'Disconnected', bill generation will skip that customer.

USE BMSSafariRwp;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.CustomersMaintenance')
    AND name = 'ConnectionStatus'
)
BEGIN
    ALTER TABLE dbo.CustomersMaintenance
    ADD ConnectionStatus NVARCHAR(50) NULL;
END
GO

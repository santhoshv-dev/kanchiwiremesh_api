BEGIN TRANSACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260826134925_Initial2', N'8.0.25');
GO

COMMIT;
GO
BEGIN TRANSACTION;
GO

EXEC sp_rename N'[SalesOrderItems].[GstRate]', N'SgstRate', N'COLUMN';
GO

EXEC sp_rename N'[Products].[GstRate]', N'SgstRate', N'COLUMN';
GO

ALTER TABLE [SalesOrderItems] ADD [CgstRate] decimal(5,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [SalesOrderItems] ADD [IgstRate] decimal(5,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [Products] ADD [CgstRate] decimal(5,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [Products] ADD [IgstRate] decimal(5,2) NOT NULL DEFAULT 0.0;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260826140632_AddExplicitGstColumns', N'8.0.25');
GO

COMMIT;
GO


using System;
using System.Collections.Generic;
using BMSBT.DTO;
using Microsoft.EntityFrameworkCore;

namespace BMSBT.Models;

public partial class BmsbtContext : DbContext
{
    public BmsbtContext()
    {
    }

    public BmsbtContext(DbContextOptions<BmsbtContext> options)
        : base(options)
    {
    }

    public DbSet<TwoMonthOutstandingBill> TwoMonthOutstandingBills { get; set; }
    public DbSet<SpecialDiscountBill> SpecialDiscountBills { get; set; }
    public DbSet<DashboardStatisticsResult> DashboardStatistics { get; set; }
    public DbSet<BillingReportData> BillingReportData { get; set; }

   

    public virtual DbSet<CustomersMaintenance> CustomersMaintenance { get; set; }
    public virtual DbSet<Configuration> Configurations { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomersDetail> CustomersDetails { get; set; }

    public virtual DbSet<ElectricityBill> ElectricityBills { get; set; }

    public virtual DbSet<MaintenanceBill> MaintenanceBills { get; set; }

    public virtual DbSet<MaintenanceTarrif> MaintenanceTarrifs { get; set; }

    public virtual DbSet<MeterType> MeterTypes { get; set; }

    public virtual DbSet<OperatorsSetup> OperatorsSetups { get; set; }
    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<ReadingSheet> ReadingSheets { get; set; }

    public virtual DbSet<TariffMaint> TariffMaints { get; set; }

    public virtual DbSet<Tarrif> Tarrifs { get; set; }

    public virtual DbSet<TaxInformation> TaxInformations { get; set; }

    public virtual DbSet<Rate> Rates { get; set; }
    public virtual DbSet<ManualRate> ManualRates { get; set; }

    public virtual DbSet<User> Users { get; set; }
    public DbSet<Fine> Fine { get; set; }
    public DbSet<AdditionalCharge> AdditionalCharges { get; set; }

    /// <summary>dbo.SubProject — keyless read for phase dropdowns.</summary>
    public virtual DbSet<SubProjectPhase> SubProjectPhases { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=172.20.229.2;Database=BMSSafariRwp;User Id=admin;Password=Pakistan@786;MultipleActiveResultSets=True;TrustServerCertificate=True;");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Configuration>(entity =>
        {
            entity.HasKey(e => e.Uid).HasName("PK__Configur__DD701264E9989C35");

            entity.ToTable("Configuration");

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.ConfigKey)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ConfigValue)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<SubProjectPhase>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable("SubProject");
            entity.Property(e => e.Project).HasMaxLength(100);
            entity.Property(e => e.PhaseNumber)
                .HasColumnName("Phase_Number")
                .HasMaxLength(100);
        });

        modelBuilder.Entity<CustomersMaintenance>(entity =>
        {
            entity.ToTable("CustomersMaintenance");
            entity.Property(e => e.CustomerNo).HasColumnName("KuickPayNo").HasMaxLength(20);
            entity.Property(e => e.SubProject).HasColumnName("PhaseNumber").HasMaxLength(50);
            entity.Property(e => e.CNICNo).HasColumnName("CNICNo").HasMaxLength(50);
            entity.Property(e => e.PloNo).HasColumnName("PloNo").HasMaxLength(100);
            entity.Property(e => e.StreetNumber).HasColumnName("StreetNumber").HasMaxLength(50);
            entity.Property(e => e.ConnectionStatus).HasMaxLength(20);
        });

        // Configure BillingReportData as keyless entity
        modelBuilder.Entity<BillingReportData>().HasNoKey();

        modelBuilder.Entity<DashboardStatisticsResult>().HasNoKey();
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(e => e.MeterNumber, "UQ__Customer__999CDEB3036F9F25").IsUnique();

            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.AccountStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Active");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.City).HasMaxLength(50);
            entity.Property(e => e.ConnectionDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.LastName).HasMaxLength(50);
            entity.Property(e => e.MeterNumber).HasMaxLength(50);
            entity.Property(e => e.OutstandingBalance)
                .HasDefaultValue(0.00m)
                .HasColumnType("decimal(10, 2)");
            entity.Property(e => e.PhoneNumber).HasMaxLength(15);
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.TariffName).HasMaxLength(50);
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ZipCode).HasMaxLength(10);
        });

        modelBuilder.Entity<CustomersDetail>(entity =>
        {
            entity.HasKey(e => e.Uid).HasName("PK__Customer__DD701264AF0F1179");

            entity.ToTable("CustomersDetail");

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.BankNo)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.BillStatus).IsUnicode(false);
            entity.Property(e => e.BillStatusMaint).IsUnicode(false);
            entity.Property(e => e.Block)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Btno)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)")
                .HasColumnName("BTNo");
            entity.Property(e => e.BtnoMaintenance)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)")
                .HasColumnName("BTNoMaintenance");
            entity.Property(e => e.Category)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.City)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Cnicno)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)")
                .HasColumnName("CNICNo");
            entity.Property(e => e.CustomerName)
                .HasMaxLength(70)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.CustomerNo)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.FatherName)
                .HasMaxLength(70)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.GeneratedMonthYear)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.InstalledOn)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.LocationSeqNo)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.MeterType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.MobileNo)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.Ntnnumber)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)")
                .HasColumnName("NTNNumber");
            entity.Property(e => e.PloNo)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.PlotType).HasMaxLength(50);
            entity.Property(e => e.Project)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Sector)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Size).HasMaxLength(50);
            entity.Property(e => e.SubProject)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TariffName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TelephoneNo)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
        });

        modelBuilder.Entity<ElectricityBill>(entity =>
        {
            entity.HasKey(e => e.Uid).HasName("PK__Electric__DD70126479D95C7E");

            entity.HasIndex(e => e.InvoiceNo, "UQ__Electric__D796B2279C42DA06").IsUnique();

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.BankDetail)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.BillingDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.BillingMonth)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.BillingYear)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Btno)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("BTNo");
            entity.Property(e => e.CreateOn)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CustomerName)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.CustomerNo)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.EnergyCoast)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Furthertax).HasColumnName("FURTHERTAX");
            entity.Property(e => e.Gst).HasColumnName("GST");
            entity.Property(e => e.InvoiceNo)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.LastUpdated)
                .HasMaxLength(100)             // match varchar(100)
                .HasColumnType("varchar(100)") // explicitly set varchar type
                .HasDefaultValueSql("CONVERT(varchar(10), GETDATE(), 120)");  // default to current date as string yyyy-MM-dd
            entity.Property(e => e.MeterNo)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.MeterType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Opc)
                .HasColumnType("decimal(18, 0)")
                .HasColumnName("OPC");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Unpaid");
            entity.Property(e => e.Ptvfee).HasColumnName("PTVFEE");
            
        });

       

        modelBuilder.Entity<MaintenanceBill>(entity =>
        {
            entity.ToTable("MaintenanceBills");
            entity.Property(e => e.Btno).HasMaxLength(50).HasColumnName("BTNo");
            entity.Property(e => e.TaxAmount).HasColumnName("current_gst");
        });

        modelBuilder.Entity<MaintenanceTarrif>(entity =>
        {
            entity.HasKey(e => e.Uid);

            entity.ToTable("MaintenanceTarrif");

            entity.Property(e => e.Uid).HasColumnName("UID");
            entity.Property(e => e.PlotType).IsUnicode(false);
            entity.Property(e => e.Project)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Size).HasMaxLength(100);
            entity.Property(e => e.Tax)
                .HasMaxLength(10)
                .IsFixedLength();
        });

        modelBuilder.Entity<MeterType>(entity =>
        {
            entity.HasKey(e => e.MeterId).HasName("PK__MeterTyp__59223BAC81CB4E1A");

            entity.ToTable("MeterType");

            entity.Property(e => e.MeterId).ValueGeneratedNever();
            entity.Property(e => e.Btno)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("BTNo");
            entity.Property(e => e.MeterNo)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.MeterType1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("MeterType");
            entity.Property(e => e.MultiplyFactor).HasColumnType("decimal(18, 0)");
            entity.Property(e => e.Uid)
                .ValueGeneratedOnAdd()
                .HasColumnName("uid");
        });

        modelBuilder.Entity<OperatorsSetup>(entity =>
        {
            entity.HasKey(e => e.OperatorID);

            entity.ToTable("OperatorsSetup");

            entity.Property(e => e.OperatorID)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("OperatorID");
            entity.Property(e => e.BankName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.BillingMonth)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.BillingYear)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.OperatorName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Uid)
                .ValueGeneratedOnAdd()
                .HasColumnName("uid");
        });

        modelBuilder.Entity<ReadingSheet>(entity =>
        {
            entity.HasKey(e => e.Uid).HasName("PK_ReadingSheet_1");

            entity.ToTable("ReadingSheet");

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.Btno)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("BTNo");
            entity.Property(e => e.CustomerNo)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.MeterType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Month)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TarrifName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Year)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<TariffMaint>(entity =>
        {
            entity.HasKey(e => e.Uid);

            entity.ToTable("TariffMaint");

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.TariffPrefix)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TariffSize)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TariffType)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Tarrif>(entity =>
        {
            entity.HasKey(e => e.Uid);

            entity.ToTable("Tarrif");

            entity.HasIndex(e => new { e.TarrifName, e.MeterType }, "IDX_Tariffs_Composite");

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.Details)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.MeterType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.MinCharges).HasColumnName("Min Charges");
            entity.Property(e => e.Nm).HasColumnName("NM");
            entity.Property(e => e.Range)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Sequence).HasMaxLength(50);
            entity.Property(e => e.Slab)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TarrifName)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TarrifType)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Rate>(entity =>
        {
            entity.HasKey(e => e.SNo).HasName("PK_Rates");
            entity.ToTable("Rates");
            entity.Property(e => e.SNo).HasColumnName("SNo").ValueGeneratedOnAdd();
            entity.Property(e => e.Phase).HasMaxLength(100);
            entity.Property(e => e.Size).HasMaxLength(50);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.UnitType).HasMaxLength(50);
            entity.Property(e => e.Misc).HasDefaultValue(0);
            entity.Property(e => e.Tax).HasDefaultValue(0);
            entity.Property(e => e.MaintCharges).HasDefaultValue(0);
            entity.Property(e => e.Total).HasDefaultValue(0);
        });

        modelBuilder.Entity<ManualRate>(entity =>
        {
            entity.HasKey(e => e.SNo).HasName("PK_ManualRates");

            entity.ToTable("ManualRates");

            entity.Property(e => e.SNo).HasColumnName("SNo");
            entity.Property(e => e.CustomerNo).HasMaxLength(50);
            entity.Property(e => e.Phase).HasMaxLength(100);
            entity.Property(e => e.Size).HasMaxLength(50);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.UnitType).HasMaxLength(50);
            entity.Property(e => e.Misc).HasDefaultValue(0);
            entity.Property(e => e.Tax).HasDefaultValue(0);
            entity.Property(e => e.MaintCharges).HasDefaultValue(0);
            entity.Property(e => e.Total)
                .HasComputedColumnSql("([Misc]+[Tax]+[MaintCharges])", stored: true)
                .ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<TaxInformation>(entity =>
        {
            entity.HasKey(e => e.TaxId).HasName("PK__TaxInfor__711BE0ACE059CFCB");

            entity.ToTable("TaxInformation");

            entity.Property(e => e.ApplicableFor)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.IsActive)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.Range)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.TaxName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.TaxRate)
                .HasDefaultValueSql("(NULL)")
                .HasColumnType("decimal(18, 0)");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Uid).HasName("PK__Users__DD7012643949B123");

            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.EmployeeId)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.Username).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

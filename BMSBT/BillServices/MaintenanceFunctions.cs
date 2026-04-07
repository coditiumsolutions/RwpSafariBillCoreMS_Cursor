using BMSBT.Models;
using BMSBT.Models.MyObjects;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BMSBT.BillServices
{
    public class MaintenanceFunctions
    {
        private readonly BmsbtContext _dbContext; // Replace with your DbContext class name
        private readonly OperatorDetailsService _operatorDetailsService;

        public MaintenanceFunctions(BmsbtContext dbContext)
        {
            _dbContext = dbContext;
        }










        public string GenerateBillForCustomer(int customerId, string currentBillingMonth, string currentBillingYear, string previousMonth, string previousYear, DateOnly? IssueDate, DateOnly? DueDate)
        {
            // Fetch customer details
            var customer = GetCustomerById(customerId);
            if (customer == null)
                return $"Customer with ID {customerId} not found.";

            // --- LotusScript: duplicate check (key = RefrenceNoBarCode + Billing_Year + Billing_Month) ---
            if (IsBillAlreadyGenerated(customer, currentBillingMonth, currentBillingYear))
            {
                customer.BillGenerationStatus = $"Bill Already Generated-{currentBillingYear}-{currentBillingMonth}";
                _dbContext.Update(customer);
                _dbContext.SaveChanges();
                return $"Bill already generated for customer {customer.CustomerName}.";
            }

            // --- LotusScript: disconnected customer check ---
            if (string.Equals(customer.ConnectionStatus?.Trim(), "Disconnected", StringComparison.OrdinalIgnoreCase))
            {
                customer.BillGenerationStatus = "Disconnected Customer";
                _dbContext.Update(customer);
                _dbContext.SaveChanges();
                return $"Disconnected customer: {customer.CustomerName}. Bill not generated.";
            }

            // Pick Rate by SubProject from CustomersMaintenance matching Rates.Phase.
            // Use:
            // - Rates.MaintCharges  -> MaintenanceBills.MaintCharges
            // - Rates.Tax           -> MaintenanceBills.TaxAmount
            // - Rates.Misc          -> MaintenanceBills.MiscCharges (and include in total bill)
            var rate = GetRateByPhase(customer.SubProject);
            if (rate == null)
            {
                customer.BillGenerationStatus = "Rates Undefined";
                _dbContext.Update(customer);
                _dbContext.SaveChanges();
                return $"Rates undefined for SubProject/Phase={customer.SubProject}. Bill not generated for {customer.CustomerName}.";
            }

            decimal maintCharges = rate.MaintCharges;
            decimal taxAmount = rate.Tax;
            decimal miscCharges = rate.Misc;

            // Check previous bill and determine arrears
            decimal? arrearsAmount = 0;
            
            var previousBill = GetPreviousBill(customer, previousMonth, previousYear);

            if (previousBill == null)
            {
                if (!IsNewCustomer(customer))
                {
                    // Last month bill not found but customer has other bill(s) -> do not generate (LotusScript: previous bill not exist)
                    customer.BillGenerationStatus = "previous bill not exist";
                    _dbContext.Update(customer);
                    _dbContext.SaveChanges();
                    return $"Previous bill not found for customer {customer.BTNo}. Bill not generated.";
                }
                // No bills at all (no bill in last 12 months / new customer) -> continue to generate with arrears 0
            }
            else
            {
                // If bill exists, check if it's unpaid (NULL or 'unpaid')
                if (string.IsNullOrEmpty(previousBill.PaymentStatus) || previousBill.PaymentStatus.Equals("unpaid", StringComparison.OrdinalIgnoreCase))
                {
                    arrearsAmount = previousBill.BillAmountAfterDueDate;
                }
            }


            // Generate a new bill with arrears (MaintCharges + MiscCharges from Rates table)
            var newBill = CreateNewBill(customer, currentBillingMonth, currentBillingYear,
                                        maintCharges, taxAmount, miscCharges,
                                        IssueDate, DueDate, arrearsAmount);

            // Assign an invoice number and update the status
            AssignInvoiceNo(newBill);

            // Update BillGenerationStatus with Month-Year
            customer.BillGenerationStatus = $"{currentBillingMonth}-{currentBillingYear}";

            UpdateGeneratedMonthYear(customer, $"Bill created for {currentBillingMonth} {currentBillingYear}");

            return $"Bill created successfully for customer {customer.CustomerName}.";
        }






        private CustomersMaintenance GetCustomerById(int customerId)
        {
            return _dbContext.CustomersMaintenance.FirstOrDefault(c => c.Uid == customerId);
        }




        public void GetPreviousBillingPeriod(string currentBillingMonth, string currentBillingYear)
        {
            // Map month numbers to their respective names
            var monthMap = new Dictionary<int, string>
                {
                  { 1, "January" }, { 2, "February" }, { 3, "March" },
                  { 4, "April" }, { 5, "May" }, { 6, "June" },
                  { 7, "July" }, { 8, "August" }, { 9, "September" },
                  { 10, "October" }, { 11, "November" }, { 12, "December" }
                };

            // Parse current month
            int currentMonth;
            if (!int.TryParse(currentBillingMonth, out currentMonth))
            {
                currentMonth = monthMap.FirstOrDefault(x => x.Value.Equals(currentBillingMonth, StringComparison.OrdinalIgnoreCase)).Key;
                if (currentMonth == 0)
                {
                    throw new ArgumentException($"Invalid month value: {currentBillingMonth}. Must be a valid integer or month name.");
                }
            }


            int currentYear;
            if (!int.TryParse(currentBillingYear, out currentYear))
            {
                throw new ArgumentException($"Invalid year value: {currentBillingYear}. Must be a valid integer.");
            }


            int previousMonth = currentMonth == 1 ? 12 : currentMonth - 1;
            int previousYear = currentMonth == 1 ? currentYear - 1 : currentYear;

            BillCreationState.PreviousMonth = monthMap[previousMonth];
            BillCreationState.PreviousYear = previousYear.ToString();

        }



        /// <summary>Look up Rates by Phase = CustomersMaintenance.SubProject. Returns null if no matching active rate. MaintCharges from this rate is used for MaintenanceBills.MaintCharges.</summary>
        private Rate? GetRateByPhase(string? subProject)
        {
            var phase = subProject?.Trim() ?? "";
            if (string.IsNullOrEmpty(phase))
                return null;

            return _dbContext.Rates
                .AsEnumerable()
                .FirstOrDefault(r =>
                    string.Equals(r.Phase?.Trim(), phase, StringComparison.OrdinalIgnoreCase));
        }

        private MaintenanceTarrif? GetTarrifDetails(CustomersMaintenance customer, string month, string year)
            {
                // Fetch the customer details based on the BTNo
                var customerDetail = _dbContext.CustomersMaintenance.FirstOrDefault(c => c.BTNo == customer.BTNo);

                // Return the matching maintenance tariff if customer details are found
                return _dbContext.MaintenanceTarrifs
                    .FirstOrDefault(t => customerDetail != null
                                         && t.PlotType == customerDetail.PlotStatus
                                         && t.Size == customerDetail.Size
                                         && t.Project == customerDetail.Project);
            }


        /// <summary>Effective BTNo on bill rows (dbo.CustomersMaintenance has BTNo only; BTNoMaintenance not in current schema).</summary>
        private static string? GetEffectiveBTNo(CustomersMaintenance customer)
        {
            return customer.BTNo?.Trim();
        }

        private MaintenanceBill? GetPreviousBill(CustomersMaintenance customer, string month, string year)
        {
            var btNo = GetEffectiveBTNo(customer);
            if (string.IsNullOrEmpty(btNo)) return null;
            return _dbContext.MaintenanceBills
                .FirstOrDefault(b =>
                    b.Btno == btNo &&
                    b.BillingMonth == month &&
                    b.BillingYear == year);
        }


        public bool IsNewCustomer(CustomersMaintenance customer)
            {
                var btNo = GetEffectiveBTNo(customer);
                if (string.IsNullOrEmpty(btNo)) return true;
                bool billExists = _dbContext.MaintenanceBills.Any(b => b.Btno == btNo);
                return !billExists;
            }

        




        private bool IsBillAlreadyGenerated(CustomersMaintenance customer, string month, string year)
        {
            var btNo = GetEffectiveBTNo(customer);
            if (string.IsNullOrEmpty(btNo)) return false;
            return _dbContext.MaintenanceBills.Any(b =>
                b.Btno == btNo && b.BillingMonth == month && b.BillingYear == year);
        }







        private MaintenanceBill CreateNewBill(
    CustomersMaintenance customer,
    string month,
    string year,
    decimal amount,
    decimal tax,
    decimal misc,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal? ArrearAmount)
        {
            // Convert inputs to decimal
            decimal amountDec = amount;
            decimal taxDec = tax;
            decimal actualArrearDec = ArrearAmount ?? 0m;
            decimal miscDec = misc;

            // Look up Maintenance fines for this BTNo/FineMonth/FineYear (use effective BTNo from CustomersMaintenance)
            var btNo = GetEffectiveBTNo(customer);
            int parsedYear;
            int.TryParse(year, out parsedYear);

            var fineTotalDec = 0m;
            decimal waterCharges = 0m;
            decimal otherCharges = 0m;
            if (!string.IsNullOrEmpty(btNo))
            {
                fineTotalDec = _dbContext.Fine
                    .Where(f =>
                        f.BTNo == btNo &&
                        f.FineMonth == month &&
                        f.FineYear == parsedYear &&
                        f.FineService == "Maintenance")
                    .Select(f => (decimal?)f.FineToCharge)
                    .Sum() ?? 0m;

                // NOTE:
                // The live database `AdditionalCharges` table (see db.txt) now has the
                // shape:
                //   - CustomerNo
                //   - ServiceName
                //   - ServiceType
                //   - Month
                //   - Year
                //
                // It no longer contains per-BTNo numeric charge columns like
                // BTNo / ChargesName / ChargesAmount. Until a numeric amount
                // column is reintroduced, we treat additional charges as 0
                // for billing calculations.
                waterCharges = 0m;
                otherCharges = 0m;
            }

            // 1) Bill due on‑time: BillAmountInDueDate = MaintCharges + TaxAmount + Arrears + Fine + WaterCharges + OtherCharges + MiscCharges
            decimal billInDueDate = Math.Round(amountDec + taxDec + actualArrearDec + fineTotalDec + waterCharges + otherCharges + miscDec, 0);

            // 2) 10% surcharge on (Charges + Tax)
            decimal baseChargesAndTax = amountDec + taxDec;
            decimal surcharge = Math.Round(baseChargesAndTax * 0.10m, 0);

            // 3) Bill after due date: BillAmountAfterDueDate = BillAmountInDueDate + BillSurcharge
            decimal billAfterDue = Math.Round(billInDueDate + surcharge, 0);

            // 4) Tax and arrears as whole numbers (rounded)
            decimal taxAmount = Math.Round(taxDec, 0);
            decimal arrearsAmt = Math.Round(actualArrearDec, 0);

            var btNoFromCustomer = GetEffectiveBTNo(customer);

            var newBill = new MaintenanceBill
            {
                CustomerNo = customer.CustomerNo,
                CustomerName = customer.CustomerName,
                Btno = btNoFromCustomer,
                BillingMonth = month,
                BillingYear = year,

                // Assign as int (DB columns are int)
                BillAmountInDueDate = (int)billInDueDate,
                BillSurcharge = (int)surcharge,
                BillAmountAfterDueDate = (int)billAfterDue,
                TaxAmount = (int)taxAmount,
                Arrears = (int)arrearsAmt,
                MaintCharges = (int)amount,
                Fine = (int)fineTotalDec,
                WaterCharges = (int)waterCharges,
                OtherCharges = (int)otherCharges,
                MiscCharges = (int)miscDec,
                IssueDate = IssueDate,
                DueDate = DueDate,

                PaymentStatus = "unpaid",
                LastUpdated = DateTime.Now,
                BillingDate = DateOnly.FromDateTime(DateTime.Now),
                MeterNo = customer.MeterNo,
                PaymentMethod = "NA",
                BankDetail = "NA",
                ValidDate = DateOnly.FromDateTime(DateTime.Now.AddMonths(1)),
                InvoiceNo = null // Will be assigned later
            };

            _dbContext.MaintenanceBills.Add(newBill);
            _dbContext.SaveChanges();

            return newBill;
        }







        private void AssignInvoiceNo(MaintenanceBill newBill)
        {
            // Per Requirement: YYYYMM + Last 5 digits of CUSTOMERNO
            // Example: 202601 + 22306 = 20260122306
            var now = DateTime.Now;
            var datePart = now.ToString("yyyyMM");
            var cust = string.IsNullOrWhiteSpace(newBill.CustomerNo) ? "00000" : newBill.CustomerNo.Trim();
            
            // Get last 5 digits of customerNo
            var lastFive = cust.Length >= 5 ? cust[^5..] : cust.PadLeft(5, '0');
            
            newBill.InvoiceNo = $"{datePart}{lastFive}";
            _dbContext.Update(newBill);
            _dbContext.SaveChanges();
        }





        private void UpdateGeneratedMonthYear(CustomersMaintenance customer, string message)
        {
            customer.BillStatusMaint = message;
            _dbContext.Update(customer);
            _dbContext.SaveChanges();
        }





      

       


     
    }
}

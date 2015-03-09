﻿using System;
using System.Collections.Generic;
using System.Linq;
using Swarmops.Common.Enums;
using Swarmops.Logic.Structure;

namespace Swarmops.Logic.Financial
{
    [Serializable]
    public class YearlyReport
    {
        public Organization Organization;
        public List<YearlyReportLine> ReportLines;
        public YearlyReportNode Totals;
        public int Year;
        private FinancialAccountType _accountType;
        private Dictionary<int, Int64>[] _singleLookups;
        private Dictionary<int, Int64>[] _treeLookups;
        private Dictionary<int, List<FinancialAccount>> _treeMap;

        public static YearlyReport Create (Organization organization, int year, FinancialAccountType accountType)
        {
            YearlyReport report = new YearlyReport();
            report.Organization = organization;
            report.Year = year;
            report._accountType = accountType;

            // Get accounts

            FinancialAccounts accounts = FinancialAccounts.ForOrganization (organization, accountType);

            // Remove unwanted accounts

            FinancialAccount resultsAccount = organization.FinancialAccounts.CostsYearlyResult;

            foreach (FinancialAccount account in accounts)
            {
                // For now, just remove the "results" account. TODO: Remove inactive accounts, too.

                if (account.Identity == resultsAccount.Identity)
                {
                    accounts.Remove (account);
                    break;
                }
            }

            // Build tree (there should be a template for this)

            report._treeMap = new Dictionary<int, List<FinancialAccount>>();

            foreach (FinancialAccount account in accounts)
            {
                if (!report._treeMap.ContainsKey (account.ParentIdentity))
                {
                    report._treeMap[account.ParentIdentity] = new List<FinancialAccount>();
                }

                report._treeMap[account.ParentIdentity].Add (account);
            }


            FinancialAccounts orderedList = new FinancialAccounts();
            // This list is guaranteed to have parents before children

            report.PopulateOrderedList (orderedList, 0); // recursively add nodes parents-first
            report.PopulateLookups (orderedList); // populate the lookup tables for results per account
            report.PopulateTotals();

            report.ReportLines = new List<YearlyReportLine>();
            report.RecurseAddLines (report.ReportLines, 0);

            // Aggregate accounts, if appropriate

            if (report._treeMap[0].Count > 3)
            {
                // regroup list

                report.AggregateAccounts();
            }

            return report;
        }


        private void AggregateAccounts()
        {
            const int assetIdentity = 1000000001;
            const int debtIdentity = 1000000002;
            const int incomeIdentity = 1000000003;
            const int costIdentity = 1000000004;

            Dictionary<FinancialAccountType, YearlyReportLine> remapLookup =
                new Dictionary<FinancialAccountType, YearlyReportLine>();

            List<YearlyReportLine> newRootLevel = new List<YearlyReportLine>();

            int equityIdentity = this._treeMap[0][0].Organization.FinancialAccounts.DebtsEquity.Identity;

            if (this._accountType == FinancialAccountType.Balance)
            {
                YearlyReportLine assetLine = new YearlyReportLine
                {AccountId = assetIdentity, AccountName = "%ASSET_ACCOUNTGROUP%"};
                YearlyReportLine debtLine = new YearlyReportLine
                {AccountId = debtIdentity, AccountName = "%DEBT_ACCOUNTGROUP%"};
                remapLookup[FinancialAccountType.Asset] = assetLine;
                remapLookup[FinancialAccountType.Debt] = debtLine;

                newRootLevel.Add (assetLine);
                newRootLevel.Add (debtLine);
            }
            else if (this._accountType == FinancialAccountType.Result)
            {
                YearlyReportLine incomeLine = new YearlyReportLine
                {AccountId = incomeIdentity, AccountName = "%INCOME_ACCOUNTGROUP%"};
                YearlyReportLine costLine = new YearlyReportLine
                {AccountId = costIdentity, AccountName = "%COST_ACCOUNTGROUP%"};
                remapLookup[FinancialAccountType.Income] = incomeLine;
                remapLookup[FinancialAccountType.Cost] = costLine;

                newRootLevel.Add (incomeLine);
                newRootLevel.Add (costLine);
            }
            else
            {
                throw new InvalidOperationException (
                    "AccountType other than Balance or Result passed to YearlyReport.AggregateAccounts()");
            }

            foreach (YearlyReportLine reportLine in this.ReportLines)
            {
                if (reportLine.AccountId == equityIdentity)
                {
                    newRootLevel.Add (reportLine);
                }
                else
                {
                    YearlyReportLine aggregateLine = remapLookup[reportLine.AccountType];
                    if (aggregateLine.Children == null)
                    {
                        aggregateLine.Children = new List<YearlyReportLine>();
                    }

                    aggregateLine.Children.Add (reportLine);

                    aggregateLine.AccountTreeValues.PreviousYear += reportLine.AccountTreeValues.PreviousYear;
                    for (int quarter = 0; quarter < 4; quarter++)
                    {
                        aggregateLine.AccountTreeValues.Quarters[quarter] +=
                            reportLine.AccountTreeValues.Quarters[quarter];
                    }

                    aggregateLine.AccountTreeValues.ThisYear += reportLine.AccountTreeValues.ThisYear;
                }
            }

            this.ReportLines = newRootLevel;
        }


        private void PopulateTotals()
        {
            this.Totals = new YearlyReportNode();

            this.Totals.PreviousYear = PopulateOneTotal (this._singleLookups[0]);
            this.Totals.ThisYear = PopulateOneTotal (this._singleLookups[5]);

            for (int quarter = 0; quarter < 4; quarter++)
            {
                this.Totals.Quarters[quarter] = PopulateOneTotal (this._singleLookups[quarter + 1]);
            }
        }

        private Int64 PopulateOneTotal (Dictionary<int, Int64> lookup)
        {
            Int64[] allValues = lookup.Values.ToArray();

            return allValues.Sum();
        }


        private void RecurseAddLines (List<YearlyReportLine> list, int renderNodeId)
        {
            foreach (FinancialAccount account in this._treeMap[renderNodeId])
            {
                YearlyReportLine newLine = new YearlyReportLine();
                newLine.AccountId = account.Identity;
                newLine.AccountName = account.Name;
                newLine.AccountType = account.AccountType;
                newLine.AccountValues = CreateYearlyReportNode (account.Identity, this._singleLookups);
                newLine.AccountTreeValues = CreateYearlyReportNode (account.Identity, this._treeLookups);

                if (this._treeMap.ContainsKey (account.Identity))
                {
                    RecurseAddLines (newLine.Children, account.Identity);
                }

                list.Add (newLine);
            }
        }


        private YearlyReportNode CreateYearlyReportNode (int accountId, Dictionary<int, Int64>[] lookup)
        {
            YearlyReportNode node = new YearlyReportNode();
            node.PreviousYear = lookup[0][accountId];

            for (int quarter = 1; quarter <= 4; quarter++)
            {
                node.Quarters[quarter - 1] = lookup[quarter][accountId];
            }

            node.ThisYear = lookup[5][accountId];
            return node;
        }


        private void PopulateOrderedList (FinancialAccounts orderedList, int renderNodeId)
        {
            foreach (FinancialAccount account in this._treeMap[renderNodeId])
            {
                orderedList.Add (account);

                if (this._treeMap.ContainsKey (account.Identity))
                {
                    PopulateOrderedList (orderedList, account.Identity); // recursive call
                }
            }
        }


        private void PopulateLookups (FinancialAccounts accounts)
        {
            this._singleLookups = new Dictionary<int, Int64>[6];
            this._treeLookups = new Dictionary<int, Int64>[6];

            for (int index = 0; index < 6; index++)
            {
                this._treeLookups[index] = new Dictionary<int, Int64>();
                this._singleLookups[index] = new Dictionary<int, Int64>();
            }

            DateTime[] quarterBoundaries =
            {
                new DateTime (this.Year, 1, 1), new DateTime (this.Year, 4, 1), new DateTime (this.Year, 7, 1),
                new DateTime (this.Year, 10, 1), new DateTime (this.Year + 1, 1, 1)
            };

            // 1) Actually, the accounts are already sorted. Or are supposed to be, anyway,
            // since FinancialAccounts.ForOrganization gets the _tree_ rather than the flat list.

            // 2) Add all values to the accounts.

            foreach (FinancialAccount account in accounts)
            {
                // Find this year's inbound

                if (this._accountType == FinancialAccountType.Result)
                {
                    this._singleLookups[0][account.Identity] = account.GetDeltaCents (
                        new DateTime (this.Year - 1, 1, 1),
                        new DateTime (this.Year, 1, 1));
                }
                else if (this._accountType == FinancialAccountType.Balance)
                {
                    this._singleLookups[0][account.Identity] = account.GetDeltaCents (new DateTime (1900, 1, 1),
                        new DateTime (this.Year, 1, 1));
                }
                else
                {
                    throw new InvalidOperationException (
                        "Can only calculate yearly reports for balance or P&L statements");
                }

                // Find quarter diffs

                for (int quarter = 0; quarter < 4; quarter++)
                {
                    this._singleLookups[quarter + 1][account.Identity] =
                        account.GetDeltaCents (quarterBoundaries[quarter],
                            quarterBoundaries[quarter + 1]);
                }

                // Find outbound

                if (this._accountType == FinancialAccountType.Result)
                {
                    this._singleLookups[5][account.Identity] = account.GetDeltaCents (new DateTime (this.Year, 1, 1),
                        new DateTime (this.Year + 1, 1, 1));
                }
                else if (this._accountType == FinancialAccountType.Balance)
                {
                    this._singleLookups[5][account.Identity] = account.GetDeltaCents (new DateTime (1900, 1, 1),
                        new DateTime (this.Year + 1, 1, 1));
                }
                else
                {
                    throw new InvalidOperationException (
                        "Can only calculate yearly reports for balance or P&L statements");
                }

                // copy to treeLookups

                for (int index = 0; index < 6; index++)
                {
                    this._treeLookups[index][account.Identity] = this._singleLookups[index][account.Identity];
                }
            }

            // 3) Add all children's values to parents

            for (int index = 0; index < 6; index++)
            {
                AddChildrenValuesToParents (this._treeLookups[index], accounts);
            }

            // Done.
        }


        private void AddChildrenValuesToParents (Dictionary<int, Int64> lookup, FinancialAccounts accounts)
        {
            // Iterate backwards and add any value to its parent's value, as they are sorted in tree order.

            for (int index = accounts.Count - 1; index >= 0; index--)
            {
                int parentFinancialAccountId = accounts[index].ParentFinancialAccountId;
                int accountId = accounts[index].Identity;

                if (parentFinancialAccountId != 0)
                {
                    lookup[parentFinancialAccountId] += lookup[accountId];
                }
            }
        }
    }


    [Serializable]
    public class YearlyReportLine
    {
        public int AccountId;
        public string AccountName;
        public YearlyReportNode AccountTreeValues;
        public FinancialAccountType AccountType;
        public YearlyReportNode AccountValues;

        public List<YearlyReportLine> Children;

        public YearlyReportLine()
        {
            this.Children = new List<YearlyReportLine>();
            this.AccountValues = new YearlyReportNode();
            this.AccountTreeValues = new YearlyReportNode();
        }
    }

    [Serializable]
    public class YearlyReportNode
    {
        public Int64 PreviousYear;
        public Int64[] Quarters;
        public Int64 ThisYear;

        public YearlyReportNode()
        {
            this.Quarters = new Int64[4];
        }
    }
}
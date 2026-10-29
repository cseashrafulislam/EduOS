using System;
using System.Collections.Generic;
using System.Linq;

namespace EduOS.Service.Services.Finance;

public sealed record RefundCreditIncomeSource(long AccountId,long? FeeHeadId,
    long? ProgramId,decimal Amount);
public sealed record RefundCreditIncomePortion(long AccountId,long? FeeHeadId,
    long? ProgramId,decimal Amount);

/// <summary>Reverses posted income proportionately in exact cents, preserving original GL attribution.</summary>
public static class RefundCreditNoteAllocator
{
    public static IReadOnlyList<RefundCreditIncomePortion> Split(
        IReadOnlyList<RefundCreditIncomeSource> sources,decimal invoiceTotal,decimal creditAmount)
    {
        if(sources.Count is <1 or >99 || invoiceTotal<=0m || creditAmount<=0m ||
            creditAmount>invoiceTotal || decimal.Round(invoiceTotal,2)!=invoiceTotal ||
            decimal.Round(creditAmount,2)!=creditAmount ||
            sources.Any(x=>x.AccountId<=0 || x.Amount<=0m ||
                decimal.Round(x.Amount,2)!=x.Amount) || sources.Sum(x=>x.Amount)!=invoiceTotal)
            throw new InvalidOperationException("Original income journal cannot support this credit note.");
        var ordered=sources.OrderBy(x=>x.AccountId).ThenBy(x=>x.FeeHeadId)
            .ThenBy(x=>x.ProgramId).ToArray();
        var remaining=decimal.ToInt64(creditAmount*100m);
        var cents=new long[ordered.Length];
        var fractions=new decimal[ordered.Length];
        for(var i=0;i<ordered.Length;i++)
        {
            var exact=creditAmount*ordered[i].Amount/invoiceTotal*100m;
            cents[i]=decimal.ToInt64(decimal.Floor(exact));
            fractions[i]=exact-cents[i];
            remaining-=cents[i];
        }
        // Largest remainder keeps each fee-head reversal within its frozen
        // original income, even when many one-cent lines share a credit note.
        foreach(var i in Enumerable.Range(0,ordered.Length)
            .OrderByDescending(i=>fractions[i]).ThenBy(i=>i))
        {
            if(remaining==0) break;
            if(cents[i]>=decimal.ToInt64(ordered[i].Amount*100m)) continue;
            cents[i]++;
            remaining--;
        }
        if(remaining!=0)
            throw new InvalidOperationException("Credit note rounding exceeded original income.");
        var result=new List<RefundCreditIncomePortion>();
        for(var i=0;i<ordered.Length;i++)
        {
            var take=cents[i]/100m;
            if(take>0m) result.Add(new RefundCreditIncomePortion(ordered[i].AccountId,
                ordered[i].FeeHeadId,ordered[i].ProgramId,take));
        }
        if(result.Sum(x=>x.Amount)!=creditAmount)
            throw new InvalidOperationException("Credit note amount could not be balanced.");
        return result;
    }
}

namespace EduOS.Service.Services.Inventory;

// A receipt line is only a possible source, never proof of one-to-one asset capitalization.
public static class AssetCapitalizationStatus
{
    public static string Classify(bool activeAsset, bool hasBook, bool campus,
        bool trackedItem, bool costPolicy, int postedCandidateLines)
    {
        if (hasBook) return "BookExists";
        if (!activeAsset) return "AssetInactive";
        if (!campus) return "CampusRequired";
        if (!trackedItem) return "AssetTrackedItemRequired";
        if (!costPolicy) return "CostPolicyRequired";
        return postedCandidateLines > 0 ? "ReviewRequired" : "NoPostedReceiptEvidence";
    }
}

using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260903150000_AddBanglaBillingCatalog")]
public partial class AddBanglaBillingCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DescriptionBangla",
            table: "SubscriptionPlans",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NameBangla",
            table: "SubscriptionPlans",
            type: "nvarchar(150)",
            maxLength: 150,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShortDescriptionBangla",
            table: "SubscriptionPlans",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DescriptionBangla",
            table: "Features",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NameBangla",
            table: "Features",
            type: "nvarchar(150)",
            maxLength: 150,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE [Features]
            SET [NameBangla] = CASE [Code]
                WHEN 'STUDENT_MGMT' THEN N'শিক্ষার্থী ব্যবস্থাপনা'
                WHEN 'CLASS_SECTION' THEN N'শ্রেণি ও শাখা'
                WHEN 'SUBJECT_MGMT' THEN N'বিষয় ব্যবস্থাপনা'
                WHEN 'CLASS_ROUTINE' THEN N'ক্লাস রুটিন'
                WHEN 'ATTENDANCE' THEN N'উপস্থিতি'
                WHEN 'EXAM_MGMT' THEN N'পরীক্ষা ব্যবস্থাপনা'
                WHEN 'MARK_ENTRY' THEN N'নম্বর এন্ট্রি'
                WHEN 'RESULT_REPORT' THEN N'ফলাফল ও রিপোর্ট কার্ড'
                WHEN 'ONLINE_EXAM' THEN N'অনলাইন পরীক্ষা'
                WHEN 'FEE_COLLECTION' THEN N'ফি আদায়'
                WHEN 'INVOICE_GEN' THEN N'ইনভয়েস তৈরি'
                WHEN 'DISCOUNT_SCHOLARSHIP' THEN N'ছাড় ও বৃত্তি'
                WHEN 'ACCOUNTING' THEN N'হিসাবরক্ষণ'
                WHEN 'EMPLOYEE_MGMT' THEN N'কর্মী ব্যবস্থাপনা'
                WHEN 'PAYROLL' THEN N'বেতন ব্যবস্থাপনা'
                WHEN 'LEAVE_MGMT' THEN N'ছুটি ব্যবস্থাপনা'
                WHEN 'SMS_NOTIFY' THEN N'SMS বিজ্ঞপ্তি'
                WHEN 'EMAIL_NOTIFY' THEN N'ইমেইল বিজ্ঞপ্তি'
                WHEN 'NOTICE_BOARD' THEN N'নোটিশ বোর্ড'
                WHEN 'PARENT_PORTAL' THEN N'অভিভাবক পোর্টাল'
                WHEN 'LIBRARY' THEN N'লাইব্রেরি'
                WHEN 'TRANSPORT' THEN N'পরিবহন'
                WHEN 'HOSTEL' THEN N'হোস্টেল'
                WHEN 'INVENTORY' THEN N'ইনভেন্টরি'
                WHEN 'MULTI_CAMPUS' THEN N'একাধিক ক্যাম্পাস'
                WHEN 'MOBILE_APP' THEN N'মোবাইল অ্যাপ'
                WHEN 'API_ACCESS' THEN N'API অ্যাক্সেস'
                WHEN 'CUSTOM_DOMAIN' THEN N'কাস্টম ডোমেইন'
                WHEN 'PRIORITY_SUPPORT' THEN N'অগ্রাধিকার সহায়তা'
                ELSE [NameBangla]
            END
            WHERE NULLIF(LTRIM(RTRIM([NameBangla])), N'') IS NULL;
            """);

        migrationBuilder.Sql(
            """
            UPDATE [SubscriptionPlans]
            SET
                [NameBangla] = CASE
                    WHEN NULLIF(LTRIM(RTRIM([NameBangla])), N'') IS NULL THEN CASE [Code]
                        WHEN 'TRIAL' THEN N'ফ্রি ট্রায়াল'
                        WHEN 'BASIC' THEN N'বেসিক'
                        WHEN 'PRO' THEN N'প্রো'
                        WHEN 'ENTERPRISE' THEN N'এন্টারপ্রাইজ'
                        ELSE [NameBangla]
                    END
                    ELSE [NameBangla]
                END,
                [DescriptionBangla] = CASE
                    WHEN NULLIF(LTRIM(RTRIM([DescriptionBangla])), N'') IS NULL THEN CASE [Code]
                        WHEN 'TRIAL' THEN N'সীমিত ফিচারসহ ১৪ দিনের ফ্রি ট্রায়াল। কোনো কার্ড প্রয়োজন নেই।'
                        WHEN 'BASIC' THEN N'ছোট স্কুল ও কোচিং সেন্টারের প্রয়োজনীয় সব ফিচার।'
                        WHEN 'PRO' THEN N'বর্ধমান প্রতিষ্ঠানের জন্য উন্নত ফিচার ও একাধিক ক্যাম্পাস সুবিধা।'
                        WHEN 'ENTERPRISE' THEN N'বড় বিশ্ববিদ্যালয় ও বহুশাখা প্রতিষ্ঠানের জন্য সর্বোচ্চ সক্ষমতা।'
                        ELSE [DescriptionBangla]
                    END
                    ELSE [DescriptionBangla]
                END,
                [ShortDescriptionBangla] = CASE
                    WHEN NULLIF(LTRIM(RTRIM([ShortDescriptionBangla])), N'') IS NULL THEN CASE [Code]
                        WHEN 'TRIAL' THEN N'EduOS ১৪ দিন বিনা মূল্যে ব্যবহার করুন'
                        WHEN 'BASIC' THEN N'ছোট প্রতিষ্ঠানের জন্য উপযোগী'
                        WHEN 'PRO' THEN N'মাঝারি প্রতিষ্ঠানের জন্য উন্নত ফিচার'
                        WHEN 'ENTERPRISE' THEN N'বড় প্রতিষ্ঠানের জন্য সর্বোচ্চ সীমা'
                        ELSE [ShortDescriptionBangla]
                    END
                    ELSE [ShortDescriptionBangla]
                END
            WHERE NULLIF(LTRIM(RTRIM([NameBangla])), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM([DescriptionBangla])), N'') IS NULL
               OR NULLIF(LTRIM(RTRIM([ShortDescriptionBangla])), N'') IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DescriptionBangla", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "NameBangla", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "ShortDescriptionBangla", table: "SubscriptionPlans");
        migrationBuilder.DropColumn(name: "DescriptionBangla", table: "Features");
        migrationBuilder.DropColumn(name: "NameBangla", table: "Features");
    }
}

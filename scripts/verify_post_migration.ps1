# Post-Migration Verification Script for TalhaCRM_ in AITStudentDB
$connStr = "Server=A2NWPLSK14SQL-v01.shr.prod.iad2.secureserver.net;Database=AITStudentDB;User Id=AITStudent;Password=Monitor@2026;Encrypt=True;TrustServerCertificate=True;"

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try {
    $conn.Open()
    Write-Output "=== POST-MIGRATION VERIFICATION (READ-ONLY) ==="

    # 1. Verify Applied Migrations
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT MigrationId, ProductVersion FROM TalhaCRM_EFMigrationsHistory ORDER BY MigrationId"
    $r = $cmd.ExecuteReader()
    Write-Output "`n[1] Applied Migrations in TalhaCRM_EFMigrationsHistory:"
    while ($r.Read()) {
        Write-Output "  * $($r.GetString(0)) ($($r.GetString(1)))"
    }
    $r.Close()

    # 2. Verify Record Counts vs Pre-Migration
    Write-Output "`n[2] Record Counts:"
    $expected = @{
        "TalhaCRM_AspNetUsers" = 3
        "TalhaCRM_AspNetRoles" = 3
        "TalhaCRM_Leads" = 7
        "TalhaCRM_LeadActivities" = 5
        "TalhaCRM_FollowUps" = 3
        "TalhaCRM_AILeadInsights" = 1
        "TalhaCRM_Notifications" = 2
        "TalhaCRM_AuditLogs" = 4
        "TalhaCRM_Tenants" = 1
        "TalhaCRM_TenantSettings" = 1
        "TalhaCRM_TenantMemberships" = 3
    }

    foreach ($table in $expected.Keys) {
        $cmd.CommandText = "IF OBJECT_ID('$table') IS NOT NULL SELECT COUNT(*) FROM [$table] ELSE SELECT -1"
        $count = $cmd.ExecuteScalar()
        Write-Output "  $($table): $count (Expected: >= $($expected[$table]))"
    }

    # 3. Verify Zero NULL or Default GUIDs in Domain Tables
    Write-Output "`n[3] Checking for Untagged TenantId Values:"
    $domainTables = @("TalhaCRM_Leads", "TalhaCRM_LeadActivities", "TalhaCRM_FollowUps", "TalhaCRM_Notifications", "TalhaCRM_AILeadInsights")
    foreach ($table in $domainTables) {
        $cmd.CommandText = "SELECT COUNT(*) FROM [$table] WHERE TenantId IS NULL OR TenantId = '00000000-0000-0000-0000-000000000000'"
        $invalid = $cmd.ExecuteScalar()
        Write-Output "  $($table) Invalid/Untagged TenantId count: $invalid"
    }

    # 4. Verify Primary Tenant Settings & Currency
    Write-Output "`n[4] Primary Tenant & Currency Check:"
    $cmd.CommandText = "SELECT TenantId, CompanyName, DefaultCurrency, IsActive FROM TalhaCRM_Tenants WHERE TenantId = '11111111-1111-1111-1111-111111111111'"
    $r = $cmd.ExecuteReader()
    while ($r.Read()) {
        Write-Output "  Tenant: $($r.GetString(1)) | Currency: $($r.GetString(2)) | Active: $($r.GetBoolean(3))"
    }
    $r.Close()

    # 5. Verify User Memberships
    Write-Output "`n[5] Mapped Memberships in TalhaCRM_TenantMemberships:"
    $cmd.CommandText = @"
        SELECT u.UserName, u.Email, tm.CompanyRole, tm.IsActive
        FROM TalhaCRM_TenantMemberships tm
        JOIN TalhaCRM_AspNetUsers u ON tm.UserId = u.Id
"@
    $r = $cmd.ExecuteReader()
    while ($r.Read()) {
        Write-Output "  Member: $($r.GetString(0)) ($($r.GetString(1))) -> Role: $($r.GetString(2)) | Active: $($r.GetBoolean(3))"
    }
    $r.Close()

    Write-Output "`n=== POST-MIGRATION VERIFICATION COMPLETE ==="
} catch {
    Write-Error $_
} finally {
    $conn.Close()
}

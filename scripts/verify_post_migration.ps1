# =============================================================================
# POST-MIGRATION VERIFICATION SCRIPT FOR TalhaCRM_ IN AITStudentDB
# Returns EXIT CODE 0 on complete success, EXIT CODE 1 on ANY failure.
# =============================================================================

$connStr = "Server=A2NWPLSK14SQL-v01.shr.prod.iad2.secureserver.net;Database=AITStudentDB;User Id=AITStudent;Password=Monitor@2026;Encrypt=True;TrustServerCertificate=True;"

$failedChecks = 0
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)

try {
    $conn.Open()
    Write-Output "=== POST-MIGRATION VERIFICATION (READ-ONLY) ==="

    # 1. Verify Applied Migrations
    Write-Output "`n[1] Checking Applied Migrations in TalhaCRM_EFMigrationsHistory..."
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT MigrationId FROM TalhaCRM_EFMigrationsHistory"
    $r = $cmd.ExecuteReader()
    $appliedMigrations = @()
    while ($r.Read()) {
        $appliedMigrations += $r.GetString(0)
    }
    $r.Close()

    if ($appliedMigrations -notcontains "20261015164702_Phase2MultiTenantCore") {
        Write-Error "CRITICAL: 20261015164702_Phase2MultiTenantCore is NOT recorded in TalhaCRM_EFMigrationsHistory!"
        $failedChecks++
    } else {
        Write-Output "  -> PASS: 20261015164702_Phase2MultiTenantCore is properly applied."
    }

    # 2. Verify Record Counts vs Pre-Migration
    Write-Output "`n[2] Checking Record Preservation..."
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
        $count = [int]$cmd.ExecuteScalar()
        if ($count -lt $expected[$table]) {
            Write-Error "FAIL: Table $table has $count records, expected at least $($expected[$table])!"
            $failedChecks++
        } else {
            Write-Output "  -> PASS: $table has $count records (expected >= $($expected[$table]))."
        }
    }

    # 3. Verify Zero NULL or Default GUIDs in Domain Tables
    Write-Output "`n[3] Checking for Untagged TenantId Values..."
    $domainTables = @("TalhaCRM_Leads", "TalhaCRM_LeadActivities", "TalhaCRM_FollowUps", "TalhaCRM_Notifications", "TalhaCRM_AILeadInsights")
    foreach ($table in $domainTables) {
        $cmd.CommandText = "SELECT COUNT(*) FROM [$table] WHERE TenantId IS NULL OR TenantId = '00000000-0000-0000-0000-000000000000'"
        $invalid = [int]$cmd.ExecuteScalar()
        if ($invalid -gt 0) {
            Write-Error "FAIL: Table $table contains $invalid untagged or default GUID TenantId records!"
            $failedChecks++
        } else {
            Write-Output "  -> PASS: Table $table has 0 untagged TenantId records."
        }
    }

    # 4. Verify Primary Tenant Settings & Currency
    Write-Output "`n[4] Checking Primary Tenant & INR Currency..."
    $cmd.CommandText = @"
        SELECT t.CompanyName, t.DefaultCurrency, t.IsActive, s.CurrencyCode, s.CurrencySymbol
        FROM TalhaCRM_Tenants t
        LEFT JOIN TalhaCRM_TenantSettings s ON t.TenantId = s.TenantId
        WHERE t.TenantId = '11111111-1111-1111-1111-111111111111'
"@
    $r = $cmd.ExecuteReader()
    if ($r.Read()) {
        $companyName = $r.GetString(0)
        $defaultCurrency = $r.GetString(1)
        $isActive = $r.GetBoolean(2)
        $settingCurrency = if ($r.IsDBNull(3)) { "" } else { $r.GetString(3) }
        $settingSymbol = if ($r.IsDBNull(4)) { "" } else { $r.GetString(4) }

        if ($defaultCurrency -ne "INR" -or $settingCurrency -ne "INR" -or !$isActive) {
            Write-Error "FAIL: Primary Tenant currency or active status is invalid! (Default: $defaultCurrency, Settings: $settingCurrency, Active: $isActive)"
            $failedChecks++
        } else {
            Write-Output "  -> PASS: Primary Tenant '$companyName' active with currency INR ($settingSymbol)."
        }
    } else {
        Write-Error "FAIL: Primary Tenant 11111111-1111-1111-1111-111111111111 not found in TalhaCRM_Tenants!"
        $failedChecks++
    }
    $r.Close()

    # 5. Verify Foreign Keys
    Write-Output "`n[5] Checking Foreign Key Constraints in SQL Server Schema..."
    $cmd.CommandText = @"
        SELECT tp.name AS ParentTable, tr.name AS ReferencedTable
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables tp ON fk.parent_object_id = tp.object_id
        INNER JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id
        WHERE tp.name IN ('TalhaCRM_Leads', 'TalhaCRM_TenantMemberships', 'TalhaCRM_TenantSettings')
          AND tr.name = 'TalhaCRM_Tenants'
"@
    $r = $cmd.ExecuteReader()
    $foundFks = @()
    while ($r.Read()) {
        $foundFks += "$($r.GetString(0))->$($r.GetString(1))"
    }
    $r.Close()

    $expectedFks = @(
        "TalhaCRM_Leads->TalhaCRM_Tenants",
        "TalhaCRM_TenantMemberships->TalhaCRM_Tenants",
        "TalhaCRM_TenantSettings->TalhaCRM_Tenants"
    )

    foreach ($fk in $expectedFks) {
        if ($foundFks -notcontains $fk) {
            Write-Error "FAIL: Required foreign key $fk missing in SQL Server schema!"
            $failedChecks++
        } else {
            Write-Output "  -> PASS: Foreign key $fk is active."
        }
    }

    # 6. Verify User Memberships
    Write-Output "`n[6] Checking User Memberships & Role Mappings..."
    $cmd.CommandText = @"
        SELECT u.UserName, tm.CompanyRole, tm.IsActive
        FROM TalhaCRM_TenantMemberships tm
        JOIN TalhaCRM_AspNetUsers u ON tm.UserId = u.Id
        WHERE tm.TenantId = '11111111-1111-1111-1111-111111111111'
"@
    $r = $cmd.ExecuteReader()
    $memberships = @{}
    while ($r.Read()) {
        $memberships[$r.GetString(0)] = @{
            Role = $r.GetString(1)
            IsActive = $r.GetBoolean(2)
        }
    }
    $r.Close()

    if ($memberships.Count -lt 3) {
        Write-Error "FAIL: Expected at least 3 active memberships, found $($memberships.Count)!"
        $failedChecks++
    } else {
        Write-Output "  -> PASS: $($memberships.Count) user memberships verified."
        foreach ($user in $memberships.Keys) {
            Write-Output "     * $user -> Role: $($memberships[$user].Role), Active: $($memberships[$user].IsActive)"
        }
    }

    # 7. Confirm Zero Platform SuperAdmin Memberships
    $cmd.CommandText = @"
        SELECT COUNT(*) 
        FROM TalhaCRM_TenantMemberships tm
        JOIN dbo.TalhaCRM_AspNetUserRoles ur ON tm.UserId = ur.UserId
        JOIN dbo.TalhaCRM_AspNetRoles r ON ur.RoleId = r.Id
        WHERE r.Name IN ('PlatformSuperAdmin', 'SuperAdmin')
"@
    $saCount = [int]$cmd.ExecuteScalar()
    if ($saCount -gt 0) {
        Write-Error "FAIL: Found $saCount illegal PlatformSuperAdmin memberships in tenant table!"
        $failedChecks++
    } else {
        Write-Output "  -> PASS: 0 PlatformSuperAdmin memberships (platform isolation verified)."
    }

} catch {
    Write-Error "EXCEPTION: $_"
    $failedChecks++
} finally {
    $conn.Close()
}

Write-Output "`n======================================================="
if ($failedChecks -gt 0) {
    Write-Error "POST-MIGRATION VERIFICATION FAILED ($failedChecks total errors)."
    exit 1
} else {
    Write-Output "ALL POST-MIGRATION CHECKS PASSED (EXIT CODE 0)."
    exit 0
}

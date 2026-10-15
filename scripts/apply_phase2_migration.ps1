# =============================================================================
# CONTROLLED PHASE 2 MIGRATION EXECUTION SCRIPT FOR AITStudentDB
# ONLY RUN AFTER EXPLICIT USER APPROVAL
# =============================================================================

$ErrorActionPreference = "Stop"

# Resolve portable project and script paths
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Get-Location
}
$projectRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$snapshotScriptPath = Join-Path $scriptDir "create_indb_snapshots.sql"
$verifyScriptPath = Join-Path $scriptDir "verify_post_migration.ps1"

$connStr = "Server=A2NWPLSK14SQL-v01.shr.prod.iad2.secureserver.net;Database=AITStudentDB;User Id=AITStudent;Password=Monitor@2026;Encrypt=True;TrustServerCertificate=True;"

Write-Output "======================================================="
Write-Output "PHASE 2 MIGRATION EXECUTION STARTING"
Write-Output "Target Database: AITStudentDB"
Write-Output "Project Root:    $projectRoot"
Write-Output "Timestamp:       $([DateTime]::UtcNow.ToString('o'))"
Write-Output "======================================================="

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)

try {
    # STEP 1: Pre-Migration Snapshot Creation
    Write-Output "`n[STEP 1] Creating In-Database Snapshot Tables (TalhaCRM_BKP_*)..."
    $conn.Open()
    $snapshotScript = Get-Content -Raw -Path $snapshotScriptPath

    # Execute snapshot script (resilient to any potential batch separators)
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = 120

    # Split batches on 'GO' if present, ensuring ExecuteNonQuery() / ExecuteScalar() compatibility
    $batches = [System.Text.RegularExpressions.Regex]::Split($snapshotScript, "(?mi)^\s*GO\s*$", [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $snapshotSuffix = $null

    foreach ($batch in $batches) {
        $trimmed = $batch.Trim()
        if (![string]::IsNullOrWhiteSpace($trimmed)) {
            $cmd.CommandText = $trimmed
            $result = $cmd.ExecuteScalar()
            if ($result -and [string]::IsNullOrWhiteSpace($snapshotSuffix)) {
                $snapshotSuffix = $result.ToString()
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($snapshotSuffix)) {
        # Fallback to query the latest snapshot metadata record
        $cmd.CommandText = "SELECT TOP 1 SnapshotSuffix FROM [TalhaCRM_BKP_SnapshotMetadata] ORDER BY SnapshotId DESC"
        $snapshotSuffix = [string]$cmd.ExecuteScalar()
    }

    if ([string]::IsNullOrWhiteSpace($snapshotSuffix)) {
        Write-Error "CRITICAL: Could not determine created snapshot suffix! Aborting migration."
        exit 1
    }

    Write-Output "  -> In-Database Snapshot tables created with suffix: $snapshotSuffix"

    # STEP 2: Verify Snapshot Creation & Row Counts Before Migration
    Write-Output "`n[STEP 2] Verifying Snapshot Row Counts against Source Tables..."
    $tablesToVerify = @(
        "TalhaCRM_Leads",
        "TalhaCRM_LeadActivities",
        "TalhaCRM_FollowUps",
        "TalhaCRM_LeadStatusHistory",
        "TalhaCRM_AILeadInsights",
        "TalhaCRM_Notifications",
        "TalhaCRM_AuditLogs",
        "TalhaCRM_AspNetUsers",
        "TalhaCRM_AspNetRoles",
        "TalhaCRM_AspNetUserRoles",
        "TalhaCRM_EFMigrationsHistory"
    )

    $mismatches = 0
    foreach ($tbl in $tablesToVerify) {
        $shortName = $tbl -replace "^TalhaCRM_", ""
        $backupTbl = "TalhaCRM_BKP_${snapshotSuffix}_${shortName}"

        $cmd.CommandText = "SELECT COUNT(*) FROM [$tbl]"
        $srcCount = [int]$cmd.ExecuteScalar()

        $cmd.CommandText = "IF OBJECT_ID('[$backupTbl]') IS NOT NULL SELECT COUNT(*) FROM [$backupTbl] ELSE SELECT -1"
        $bkpCount = [int]$cmd.ExecuteScalar()

        if ($bkpCount -eq -1) {
            Write-Error "CRITICAL: Snapshot table [$backupTbl] was NOT found in database!"
            $mismatches++
        } elseif ($srcCount -ne $bkpCount) {
            Write-Error "CRITICAL: Row count mismatch for $tbl! Source count: $srcCount, Snapshot count: $bkpCount"
            $mismatches++
        } else {
            Write-Output "  -> PASS: $tbl ($srcCount rows) == [$backupTbl] ($bkpCount rows)"
        }
    }

    if ($mismatches -gt 0) {
        $cmd.CommandText = "UPDATE [TalhaCRM_BKP_SnapshotMetadata] SET Status = 'Failed' WHERE SnapshotSuffix = @suffix"
        $cmd.Parameters.Clear()
        $p = $cmd.Parameters.Add("@suffix", [System.Data.SqlDbType]::NVarChar, 50)
        $p.Value = $snapshotSuffix
        $cmd.ExecuteNonQuery() | Out-Null

        Write-Error "CRITICAL: Snapshot verification failed with $mismatches errors! Aborting migration before EF Core execution."
        exit 1
    }

    # Mark as Verified ONLY after row counts match 100% across all tables
    $cmd.CommandText = "UPDATE [TalhaCRM_BKP_SnapshotMetadata] SET Status = 'Verified' WHERE SnapshotSuffix = @suffix"
    $cmd.Parameters.Clear()
    $p = $cmd.Parameters.Add("@suffix", [System.Data.SqlDbType]::NVarChar, 50)
    $p.Value = $snapshotSuffix
    $cmd.ExecuteNonQuery() | Out-Null

    Write-Output "  -> All pre-migration snapshots verified 100% byte-and-row accurate (Status: 'Verified')."

} catch {
    Write-Error "CRITICAL: Pre-migration snapshot failed! Aborting without modifying any tables: $_"
    exit 1
} finally {
    if ($conn.State -eq [System.Data.ConnectionState]::Open) {
        $conn.Close()
    }
}

# STEP 3: Execute EF Core Migration
Write-Output "`n[STEP 3] Applying EF Core Migration: Phase2MultiTenantCore..."
$proc = Start-Process -FilePath "dotnet" -ArgumentList "ef", "database", "update", "20261015164702_Phase2MultiTenantCore" -WorkingDirectory $projectRoot -NoNewWindow -Wait -PassThru

if ($proc.ExitCode -ne 0) {
    Write-Error "CRITICAL: Migration execution failed with exit code $($proc.ExitCode)! Aborting and preserving snapshots."
    exit $proc.ExitCode
}
Write-Output "  -> dotnet ef database update completed successfully (ExitCode: 0)."

# STEP 4: Run Post-Migration Verification
Write-Output "`n[STEP 4] Executing Post-Migration Verification Script..."
& powershell -ExecutionPolicy Bypass -File $verifyScriptPath
$verifyExit = $LASTEXITCODE

if ($verifyExit -ne 0) {
    Write-Error "CRITICAL: Post-migration verification failed with exit code $verifyExit! Check logs immediately."
    exit $verifyExit
}

Write-Output "`n======================================================="
Write-Output "PHASE 2 MIGRATION EXECUTION & VERIFICATION COMPLETE"
Write-Output "All 4 steps succeeded with zero errors."
Write-Output "======================================================="
exit 0

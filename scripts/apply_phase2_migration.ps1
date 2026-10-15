# =============================================================================
# CONTROLLED PHASE 2 MIGRATION EXECUTION SCRIPT FOR AITStudentDB
# ONLY RUN AFTER EXPLICIT USER APPROVAL
# =============================================================================

$ErrorActionPreference = "Stop"

$connStr = "Server=A2NWPLSK14SQL-v01.shr.prod.iad2.secureserver.net;Database=AITStudentDB;User Id=AITStudent;Password=Monitor@2026;Encrypt=True;TrustServerCertificate=True;"

Write-Output "======================================================="
Write-Output "PHASE 2 MIGRATION EXECUTION STARTING"
Write-Output "Target Database: AITStudentDB"
Write-Output "Timestamp: $([DateTime]::UtcNow.ToString('o'))"
Write-Output "======================================================="

# STEP 1: Pre-Migration Snapshot Creation
Write-Output "`n[STEP 1] Creating In-Database Snapshot Tables (TalhaCRM_BKP_*)..."
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try {
    $conn.Open()
    $snapshotScript = Get-Content -Raw -Path "C:\Users\DELL\Desktop\CRM\AI_Powered_CRM\scripts\create_indb_snapshots.sql"
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $snapshotScript
    $cmd.ExecuteNonQuery() | Out-Null
    Write-Output "  -> In-Database Snapshot tables created and verified."

    # STEP 2: Record Pre-Migration Row Counts
    Write-Output "`n[STEP 2] Recording Pre-Migration Baseline Counts..."
    $tables = @("TalhaCRM_Leads", "TalhaCRM_LeadActivities", "TalhaCRM_FollowUps", "TalhaCRM_AILeadInsights", "TalhaCRM_Notifications", "TalhaCRM_AuditLogs", "TalhaCRM_AspNetUsers")
    $preCounts = @{}
    foreach ($t in $tables) {
        $cmd.CommandText = "SELECT COUNT(*) FROM [$t]"
        $count = $cmd.ExecuteScalar()
        $preCounts[$t] = $count
        Write-Output "  * $t: $count rows"
    }
} catch {
    Write-Error "CRITICAL: Pre-migration snapshot failed! Aborting without modifying any tables: $_"
    exit 1
} finally {
    $conn.Close()
}

# STEP 3: Execute EF Core Migration
Write-Output "`n[STEP 3] Applying EF Core Migration: Phase2MultiTenantCore..."
$proc = Start-Process -FilePath "dotnet" -ArgumentList "ef", "database", "update", "20261015164702_Phase2MultiTenantCore" -WorkingDirectory "C:\Users\DELL\Desktop\CRM\AI_Powered_CRM" -NoNewWindow -Wait -PassThru

if ($proc.ExitCode -ne 0) {
    Write-Error "CRITICAL: Migration execution failed with exit code $($proc.ExitCode)! Aborting and preserving snapshots."
    exit $proc.ExitCode
}
Write-Output "  -> dotnet ef database update completed successfully (ExitCode: 0)."

# STEP 4: Run Post-Migration Verification
Write-Output "`n[STEP 4] Executing Post-Migration Verification Script..."
& powershell -ExecutionPolicy Bypass -File "C:\Users\DELL\Desktop\CRM\AI_Powered_CRM\scripts\verify_post_migration.ps1"
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

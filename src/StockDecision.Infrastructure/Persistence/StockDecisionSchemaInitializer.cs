using Microsoft.EntityFrameworkCore;
using System.Data;

namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 为“原始表已存在、领域表缺失”的混合场景补齐领域层表结构。
/// </summary>
public static class StockDecisionSchemaInitializer
{
    /// <summary>
    /// 安全补建领域层与策略层表，不依赖 EnsureCreated 的全量初始化语义。
    /// </summary>
    public static async Task EnsureDomainTablesAsync(StockDecisionDbContext dbContext, CancellationToken cancellationToken = default)
    {
        foreach (var statement in CreateTableStatements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await EnsureSnapshotVersionColumnsAsync(dbContext, cancellationToken);
    }

    /// <summary>
    /// 为旧表补齐 snapshot_version 列，并在需要时调整主键。
    /// </summary>
    private static async Task EnsureSnapshotVersionColumnsAsync(StockDecisionDbContext dbContext, CancellationToken cancellationToken)
    {
        await EnsureSnapshotVersionColumnAndPrimaryKeyAsync(
            dbContext,
            tableName: "strategy_indicator_snapshots",
            addColumnSql: "ALTER TABLE strategy_indicator_snapshots ADD COLUMN snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final' AFTER trade_date",
            primaryKeyColumns: ["stock_code", "trade_date", "snapshot_version"],
            cancellationToken);

        await EnsureSnapshotVersionColumnAndPrimaryKeyAsync(
            dbContext,
            tableName: "strategy_market_regimes",
            addColumnSql: "ALTER TABLE strategy_market_regimes ADD COLUMN snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final' AFTER trade_date",
            primaryKeyColumns: ["trade_date", "snapshot_version"],
            cancellationToken);

        await EnsureSnapshotVersionColumnAndPrimaryKeyAsync(
            dbContext,
            tableName: "strategy_candidates",
            addColumnSql: "ALTER TABLE strategy_candidates ADD COLUMN snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final' AFTER trade_date",
            primaryKeyColumns: ["trade_date", "snapshot_version", "stock_code", "strategy_type"],
            cancellationToken);

        await EnsureSnapshotVersionColumnAndPrimaryKeyAsync(
            dbContext,
            tableName: "strategy_trade_signals",
            addColumnSql: "ALTER TABLE strategy_trade_signals ADD COLUMN snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final' AFTER trade_date",
            primaryKeyColumns: ["trade_date", "snapshot_version", "stock_code", "strategy_type"],
            cancellationToken);

        if (!await ColumnExistsAsync(dbContext, "domain_sync_runs", "snapshot_version", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE domain_sync_runs ADD COLUMN snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final' AFTER trigger_kind",
                cancellationToken);
        }
    }

    /// <summary>
    /// 按需补齐快照版本列并调整主键，避免对已正确结构重复执行破坏性 DDL。
    /// </summary>
    private static async Task EnsureSnapshotVersionColumnAndPrimaryKeyAsync(
        StockDecisionDbContext dbContext,
        string tableName,
        string addColumnSql,
        IReadOnlyList<string> primaryKeyColumns,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(dbContext, tableName, "snapshot_version", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(addColumnSql, cancellationToken);
        }

        if (!await PrimaryKeyMatchesAsync(dbContext, tableName, primaryKeyColumns, cancellationToken))
        {
            var primaryKeySql = $"ALTER TABLE {tableName} DROP PRIMARY KEY, ADD PRIMARY KEY ({string.Join(", ", primaryKeyColumns)})";
            await dbContext.Database.ExecuteSqlRawAsync(primaryKeySql, cancellationToken);
        }
    }

    /// <summary>
    /// 检查指定列是否已存在。
    /// </summary>
    private static async Task<bool> ColumnExistsAsync(
        StockDecisionDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = @tableName
                  AND column_name = @columnName
                """;

            AddParameter(command, "@tableName", tableName);
            AddParameter(command, "@columnName", columnName);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// 检查当前主键列顺序是否已经符合预期。
    /// </summary>
    private static async Task<bool> PrimaryKeyMatchesAsync(
        StockDecisionDbContext dbContext,
        string tableName,
        IReadOnlyList<string> expectedColumns,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT column_name
                FROM information_schema.key_column_usage
                WHERE table_schema = DATABASE()
                  AND table_name = @tableName
                  AND constraint_name = 'PRIMARY'
                ORDER BY ordinal_position
                """;

            AddParameter(command, "@tableName", tableName);
            var actualColumns = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                actualColumns.Add(reader.GetString(0));
            }

            return actualColumns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// 为 ADO 命令补充参数，避免拼接元数据查询条件。
    /// </summary>
    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static readonly string[] CreateTableStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS market_stock_profiles (
            stock_code VARCHAR(16) NOT NULL,
            trade_date DATE NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            industry_name VARCHAR(64) NULL,
            is_active TINYINT(1) NOT NULL,
            is_st TINYINT(1) NOT NULL,
            is_delisting_risk TINYINT(1) NOT NULL,
            list_date DATE NULL,
            latest_price DECIMAL(18,4) NULL,
            pe DECIMAL(18,4) NULL,
            pb DECIMAL(18,4) NULL,
            free_float_market_cap DECIMAL(20,2) NULL,
            turnover_rate DECIMAL(10,4) NULL,
            average_amount20d DECIMAL(20,2) NULL,
            PRIMARY KEY (stock_code, trade_date)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS market_daily_bars (
            stock_code VARCHAR(16) NOT NULL,
            trade_date DATE NOT NULL,
            open DECIMAL(18,4) NOT NULL,
            high DECIMAL(18,4) NOT NULL,
            low DECIMAL(18,4) NOT NULL,
            close DECIMAL(18,4) NOT NULL,
            volume BIGINT NOT NULL,
            amount DECIMAL(20,2) NOT NULL,
            pct_change DECIMAL(10,4) NULL,
            turnover_rate DECIMAL(10,4) NULL,
            PRIMARY KEY (stock_code, trade_date)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS market_index_bars (
            index_code VARCHAR(16) NOT NULL,
            trade_date DATE NOT NULL,
            index_name VARCHAR(64) NOT NULL,
            close DECIMAL(18,4) NOT NULL,
            PRIMARY KEY (index_code, trade_date)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS market_industry_daily_stats (
            industry_code VARCHAR(32) NOT NULL,
            trade_date DATE NOT NULL,
            industry_name VARCHAR(64) NOT NULL,
            pct_change_20d DECIMAL(10,4) NULL,
            rank_20d INT NULL,
            PRIMARY KEY (industry_code, trade_date)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS market_financial_snapshots (
            stock_code VARCHAR(16) NOT NULL,
            report_date DATE NOT NULL,
            pe DECIMAL(18,4) NULL,
            pb DECIMAL(18,4) NULL,
            roe DECIMAL(18,4) NULL,
            revenue_yoy DECIMAL(18,4) NULL,
            net_profit_yoy DECIMAL(18,4) NULL,
            free_float_market_cap DECIMAL(20,2) NULL,
            PRIMARY KEY (stock_code, report_date)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS strategy_indicator_snapshots (
            stock_code VARCHAR(16) NOT NULL,
            trade_date DATE NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final',
            close DECIMAL(18,4) NOT NULL,
            ma20 DECIMAL(18,4) NOT NULL,
            ma60 DECIMAL(18,4) NOT NULL,
            ma120 DECIMAL(18,4) NOT NULL,
            atr14 DECIMAL(18,4) NOT NULL,
            return20d DECIMAL(10,4) NOT NULL,
            return60d DECIMAL(10,4) NOT NULL,
            relative_strength_score DECIMAL(10,4) NOT NULL,
            is20_day_breakout TINYINT(1) NOT NULL,
            is_ma20_upward TINYINT(1) NOT NULL,
            is_bullish_stacked TINYINT(1) NOT NULL,
            distance_to_ma20_pct DECIMAL(10,4) NOT NULL,
            turnover_rate DECIMAL(10,4) NULL,
            PRIMARY KEY (stock_code, trade_date, snapshot_version)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS strategy_market_regimes (
            trade_date DATE NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final',
            regime VARCHAR(32) NOT NULL,
            confirmed_index_count INT NOT NULL,
            is_signal_eligible TINYINT(1) NOT NULL,
            summary VARCHAR(256) NOT NULL,
            PRIMARY KEY (trade_date, snapshot_version)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS strategy_candidates (
            trade_date DATE NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final',
            stock_code VARCHAR(16) NOT NULL,
            strategy_type VARCHAR(32) NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            industry_name VARCHAR(64) NULL,
            grade VARCHAR(8) NOT NULL,
            is_tradable TINYINT(1) NOT NULL,
            total_score DECIMAL(10,4) NOT NULL,
            relative_strength_score_part DECIMAL(10,4) NOT NULL,
            trend_score_part DECIMAL(10,4) NOT NULL,
            volume_price_score_part DECIMAL(10,4) NOT NULL,
            fundamental_score_part DECIMAL(10,4) NOT NULL,
            close DECIMAL(18,4) NOT NULL,
            ma20 DECIMAL(18,4) NOT NULL,
            ma60 DECIMAL(18,4) NOT NULL,
            ma120 DECIMAL(18,4) NOT NULL,
            atr14 DECIMAL(18,4) NOT NULL,
            relative_strength_score DECIMAL(10,4) NOT NULL,
            pe DECIMAL(18,4) NULL,
            pb DECIMAL(18,4) NULL,
            roe DECIMAL(18,4) NULL,
            stop_loss_price DECIMAL(18,4) NOT NULL,
            target_price DECIMAL(18,4) NOT NULL,
            risk_reward_ratio DECIMAL(10,4) NOT NULL,
            explanation LONGTEXT NOT NULL,
            PRIMARY KEY (trade_date, snapshot_version, stock_code, strategy_type)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS strategy_trade_signals (
            trade_date DATE NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final',
            stock_code VARCHAR(16) NOT NULL,
            strategy_type VARCHAR(32) NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            industry_name VARCHAR(64) NULL,
            total_score DECIMAL(10,4) NOT NULL,
            relative_strength_score_part DECIMAL(10,4) NOT NULL,
            trend_score_part DECIMAL(10,4) NOT NULL,
            volume_price_score_part DECIMAL(10,4) NOT NULL,
            fundamental_score_part DECIMAL(10,4) NOT NULL,
            trigger_price DECIMAL(18,4) NOT NULL,
            stop_loss_price DECIMAL(18,4) NOT NULL,
            target_price DECIMAL(18,4) NOT NULL,
            risk_reward_ratio DECIMAL(10,4) NOT NULL,
            suggested_capital DECIMAL(18,2) NOT NULL,
            estimated_shares INT NOT NULL,
            explanation LONGTEXT NOT NULL,
            generated_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (trade_date, snapshot_version, stock_code, strategy_type)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS domain_sync_runs (
            id INT NOT NULL AUTO_INCREMENT,
            job_name VARCHAR(64) NOT NULL,
            trigger_kind VARCHAR(32) NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL DEFAULT 'end_of_day_final',
            status VARCHAR(32) NOT NULL,
            data_updated TINYINT(1) NOT NULL,
            is_signal_eligible TINYINT(1) NOT NULL,
            effective_trade_date DATE NULL,
            financial_report_date DATE NULL,
            started_at DATETIME(6) NOT NULL,
            finished_at DATETIME(6) NULL,
            summary VARCHAR(512) NOT NULL,
            created_at DATETIME(6) NOT NULL,
            PRIMARY KEY (id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS simulated_positions (
            id INT NOT NULL AUTO_INCREMENT,
            stock_code VARCHAR(16) NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            industry_name VARCHAR(64) NULL,
            strategy_type VARCHAR(32) NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL,
            trade_date DATE NOT NULL,
            entry_price DECIMAL(18,4) NOT NULL,
            stop_loss_price DECIMAL(18,4) NOT NULL,
            target_price DECIMAL(18,4) NOT NULL,
            quantity INT NOT NULL,
            invested_capital DECIMAL(18,2) NOT NULL,
            status VARCHAR(32) NOT NULL,
            opened_at_utc DATETIME(6) NOT NULL,
            closed_at_utc DATETIME(6) NULL,
            closed_trade_date DATE NULL,
            exit_price DECIMAL(18,4) NULL,
            realized_profit_amount DECIMAL(18,2) NULL,
            realized_profit_pct DECIMAL(10,4) NULL,
            notes LONGTEXT NULL,
            PRIMARY KEY (id),
            INDEX idx_simulated_positions_status (status),
            INDEX idx_simulated_positions_code (stock_code)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS simulated_trade_histories (
            id INT NOT NULL AUTO_INCREMENT,
            position_id INT NOT NULL,
            action_type VARCHAR(16) NOT NULL,
            stock_code VARCHAR(16) NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            trade_date DATE NOT NULL,
            price DECIMAL(18,4) NOT NULL,
            quantity INT NOT NULL,
            amount DECIMAL(18,2) NOT NULL,
            summary VARCHAR(256) NOT NULL,
            created_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (id),
            INDEX idx_simulated_trade_histories_position_id (position_id),
            INDEX idx_simulated_trade_histories_created_at_utc (created_at_utc)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS backtest_runs (
            id INT NOT NULL AUTO_INCREMENT,
            strategy_version VARCHAR(32) NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL,
            start_date DATE NOT NULL,
            end_date DATE NOT NULL,
            sample_trade_count INT NOT NULL,
            win_rate_pct DECIMAL(10,4) NOT NULL,
            average_return_pct DECIMAL(10,4) NOT NULL,
            average_max_gain_pct DECIMAL(10,4) NOT NULL,
            average_max_drawdown_pct DECIMAL(10,4) NOT NULL,
            profit_loss_ratio DECIMAL(10,4) NOT NULL,
            max_drawdown_pct DECIMAL(10,4) NOT NULL,
            total_return_pct DECIMAL(10,4) NOT NULL,
            average_holding_days DECIMAL(10,4) NOT NULL,
            created_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (id),
            INDEX idx_backtest_runs_created_at_utc (created_at_utc)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS backtest_trade_results (
            id INT NOT NULL AUTO_INCREMENT,
            backtest_run_id INT NOT NULL,
            trade_date DATE NOT NULL,
            stock_code VARCHAR(16) NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            strategy_type VARCHAR(32) NOT NULL,
            entry_price DECIMAL(18,4) NOT NULL,
            exit_price DECIMAL(18,4) NOT NULL,
            return_pct DECIMAL(10,4) NOT NULL,
            max_gain_pct DECIMAL(10,4) NOT NULL,
            max_drawdown_pct DECIMAL(10,4) NOT NULL,
            hit_target TINYINT(1) NOT NULL,
            hit_stop_loss TINYINT(1) NOT NULL,
            PRIMARY KEY (id),
            INDEX idx_backtest_trade_results_run_id (backtest_run_id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS backtest_equity_points (
            id INT NOT NULL AUTO_INCREMENT,
            backtest_run_id INT NOT NULL,
            trade_date DATE NOT NULL,
            equity DECIMAL(18,4) NOT NULL,
            return_pct DECIMAL(10,4) NOT NULL,
            PRIMARY KEY (id),
            INDEX idx_backtest_equity_points_run_id (backtest_run_id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS learning_reviews (
            id INT NOT NULL AUTO_INCREMENT,
            position_id INT NULL,
            stock_code VARCHAR(16) NOT NULL,
            stock_name VARCHAR(64) NOT NULL,
            trade_date DATE NOT NULL,
            snapshot_version VARCHAR(32) NOT NULL,
            buy_reason LONGTEXT NOT NULL,
            market_context LONGTEXT NOT NULL,
            execution_discipline LONGTEXT NOT NULL,
            result_summary LONGTEXT NOT NULL,
            improvement_plan LONGTEXT NOT NULL,
            created_at_utc DATETIME(6) NOT NULL,
            updated_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (id),
            INDEX idx_learning_reviews_stock_code (stock_code),
            INDEX idx_learning_reviews_updated_at_utc (updated_at_utc)
        )
        """
    ];
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;
using Npgsql;

namespace McpAgentWorkshop.McpServer.Tools;

[McpServerPromptType]
public class SalesTools
{
    private static readonly ActivitySource activitySource = new("McpAgentWorkshop.McpServer.Tools.SalesTools");
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };


    [McpServerTool, Description("Always fetch table schemas first, use exact column names, join related tables for clarity, aggregate results, limit output to 20 rows, and explain that results are limited for readability.")]
    public async Task<string> ExecuteSalesQueryAsync(
        NpgsqlConnection connection,
        ILogger<SalesTools> logger,
        IHttpContextAccessor httpContextAccessor,
        [Description("A well-formed PostgreSQL query.")] string query
    )
    {
        if (string.IsNullOrEmpty(query))
        {
            logger.LogError("Query cannot be null or empty.");
            throw new ArgumentException("Query cannot be null or empty.", nameof(query));
        }

        var rlsUserId = httpContextAccessor.GetRequestUserId();
        logger.LogInformation("RLS User ID: {RlsUserId}", rlsUserId);

        try
        {
            using var activity = activitySource.StartActivity("ExecuteSalesQuery", ActivityKind.Server);

            if (activity is not null)
            {
                activity.SetTag("app.manager_id", rlsUserId);
                activity.SetTag("db.query", query);
                activity.DisplayName = "PostgreSQL:ExecuteSalesQuery";
            }

            await using var cmd = new NpgsqlCommand("SELECT set_config('app.current_rls_user_id', @rlsUserId, false)", connection);
            cmd.Parameters.AddWithValue("rlsUserId", rlsUserId ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();

            await using var queryCmd = new NpgsqlCommand(query, connection);
            await using var reader = await queryCmd.ExecuteReaderAsync();

            var results = new List<Dictionary<string, object?>>();
            var columns = new List<string>();

            if (reader.HasRows)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                    columns.Add(reader.GetName(i));

                int rowCount = 0;
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    results.Add(row);
                    rowCount++;
                }

                activity?.SetTag("db.results.count", rowCount);

                return JsonSerializer.Serialize(new
                {
                    results,
                    row_count = rowCount,
                    columns
                }, jsonOptions);
            }
            else
            {
                activity?.SetTag("db.results.count", 0);

                return JsonSerializer.Serialize(new
                {
                    results = new List<object>(),
                    row_count = 0,
                    columns = new List<string>(),
                    message = "The query returned no results. Try a different question."
                }, jsonOptions);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while executing the sales query.");
            throw new InvalidOperationException("An error occurred while executing the sales query.", ex);
        }
    }
}
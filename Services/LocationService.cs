using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Data.Common;
using System.Text.Json.Serialization;
using System.Net;

namespace PubSearchSite.Services;

public interface ILocationService
{
    Task<IReadOnlyList<string>> GetLmSitesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetConsideredSitesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCerclaSitesAsync(CancellationToken cancellationToken = default);
    Task<DocumentResult> GetLmDocumentsAsync(string site, CancellationToken cancellationToken = default);
    Task<SiteDetails?> GetConsideredSiteDetailsAsync(string filterName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentItem>> GetConsideredSiteDocumentsAsync(string filterName, CancellationToken cancellationToken = default);
}

public class LocationService : ILocationService
{
    private readonly string _connectionString;
    private readonly ILogger<LocationService> _logger;

    public LocationService(IConfiguration configuration, ILogger<LocationService> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("PubSearch")
            ?? throw new InvalidOperationException("Connection string 'PubSearch' is missing.");
    }

    // Normalizes display names by removing underscores, commas, duplicate spaces,
    // trailing state/code suffixes (e.g., " - NM.05") and trailing numeric codes (e.g., " - 037"),
    // and trims trailing punctuation.
    private static string NormalizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var cleaned = value;
        cleaned = Regex.Replace(cleaned, "_+", " ");                  // underscores -> space
        cleaned = cleaned.Replace(",", "");                           // drop commas
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();         // collapse spaces
        cleaned = Regex.Replace(cleaned, @"\s*-\s*[A-Z]{2}\.?\-?\d{1,3}$", "", RegexOptions.IgnoreCase); // trim trailing state/code
        cleaned = Regex.Replace(cleaned, @"\s*-\s*\d{1,4}$", "", RegexOptions.IgnoreCase);               // trim trailing numeric code
        cleaned = Regex.Replace(cleaned, @"\.+$", "");                // trim trailing periods
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();         // final collapse/trim

        return cleaned.Trim();
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        
        // Remove HTML tags using regex
        var stripped = Regex.Replace(html, "<.*?>", string.Empty);
        
        // Decode HTML entities
        stripped = WebUtility.HtmlDecode(stripped);
        
        // Clean up extra whitespace
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
        
        return stripped;
    }

    private static string SafeGetString(DbDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return string.Empty;
            
            var value = reader.GetValue(ordinal);
            if (value is DateTime dateValue)
            {
                return dateValue.ToString("M/d/yyyy");
            }
            var stringValue = value.ToString() ?? string.Empty;
            return StripHtmlTags(stringValue);
        }
        catch (IndexOutOfRangeException)
        {
            return string.Empty;
        }
    }

    public async Task<IReadOnlyList<string>> GetLmSitesAsync(CancellationToken cancellationToken = default)
    {
        var allSites = new List<string>();
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Site FROM LMandConsideredSites";
            command.CommandType = CommandType.Text;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var siteValue = SafeGetString(reader, "Site");
                var value = NormalizeDisplayName(siteValue);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    allSites.Add(value);
                }
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load Sites from dbo.LMandConsideredSites. Check connection string and permissions.");
            // Return empty list to keep the page running; UI will just show blank dropdown.
        }

        // Filter: Include values with or without underscore (_), exclude values with hyphens (-) or spaces
        var finalSites = allSites
            .Where(s => !s.Contains('-') && !s.Contains(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        return finalSites;
    }

    public async Task<IReadOnlyList<string>> GetConsideredSitesAsync(CancellationToken cancellationToken = default)
    {
        var sites = new List<string>();
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT [Filter_x0020_Name] FROM ConsideredSiteDetails";
            command.CommandType = CommandType.Text;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var filterValue = SafeGetString(reader, "Filter_x0020_Name");
                var value = NormalizeDisplayName(filterValue);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sites.Add(value);
                }
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load Filter_x0020_Name from dbo.ConsideredSiteDetails. Check connection string and permissions.");
        }

        // Remove duplicates and sort
        var finalSites = sites
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        return finalSites;
    }

    public async Task<IReadOnlyList<string>> GetCerclaSitesAsync(CancellationToken cancellationToken = default)
    {
        var sites = new List<string>();
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT [Site]
                FROM CERCLA
                WHERE [Site] IS NOT NULL AND LTRIM(RTRIM([Site])) <> ''
                ORDER BY [Site]
            """;
            command.CommandType = CommandType.Text;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var value = NormalizeDisplayName(reader.GetString(0));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sites.Add(value);
                }
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load Site from dbo.CERCLA. Check connection string and permissions.");
        }

        return sites;
    }

    public async Task<DocumentResult> GetLmDocumentsAsync(string site, CancellationToken cancellationToken = default)
    {
        var allDocs = new List<DocumentItem>();
        var keyDocs = new List<DocumentItem>();

        if (string.IsNullOrWhiteSpace(site))
        {
            return new DocumentResult(Array.Empty<DocumentItem>(), Array.Empty<DocumentItem>());
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Check if LegacyDateCreated column exists
            bool hasDateColumn = false;
            try
            {
                await using var checkCommand = connection.CreateCommand();
                checkCommand.CommandText = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'LMandConsideredSites' 
                      AND COLUMN_NAME = 'LegacyDateCreated'";
                var result = await checkCommand.ExecuteScalarAsync(cancellationToken);
                hasDateColumn = result != null && Convert.ToInt32(result) > 0;
            }
            catch
            {
                hasDateColumn = false;
            }

            // Build query based on available columns
            string dateSelect = hasDateColumn 
                ? "CASE WHEN [LegacyDateCreated] IS NOT NULL THEN CONVERT(VARCHAR(50), [LegacyDateCreated], 101) ELSE '' END AS DatePosted"
                : "'' AS DatePosted";
            string orderBy = hasDateColumn ? "ORDER BY [LegacyDateCreated] DESC" : "ORDER BY [Title]";

            // Get ALL documents for Site Documents table (Key_x0020_Document = 0 or 1)
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT 
                    COALESCE([DocDesc], [Title], [FileLeafRef]) AS Title,
                    COALESCE([Document_x0020_Category], '') AS DocumentCategory,
                    COALESCE([State], '') AS State,
                    {dateSelect},
                    [FileLeafRef],
                    COALESCE([Key_x0020_Document], 0) AS IsKeyDocument
                FROM LMandConsideredSites
                WHERE [Site] = @site
                  AND (COALESCE([DocDesc], [Title], [FileLeafRef]) IS NOT NULL AND COALESCE([DocDesc], [Title], [FileLeafRef]) <> '')
                {orderBy}";
            command.CommandType = CommandType.Text;
            command.Parameters.AddWithValue("@site", site);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var title = SafeGetString(reader, "Title").Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;

                var docItem = new DocumentItem
                {
                    Title = title,
                    Category = SafeGetString(reader, "DocumentCategory").Trim(),
                    State = SafeGetString(reader, "State").Trim(),
                    DatePosted = SafeGetString(reader, "DatePosted").Trim(),
                    FilePath = SafeGetString(reader, "FileLeafRef").Trim()
                };

                // Add to Site Documents (all documents from same table)
                allDocs.Add(docItem);

                // If Key_x0020_Document = 1, also add to Key Documents (from same table)
                try
                {
                    var isKeyDocValue = SafeGetString(reader, "IsKeyDocument");
                    if (int.TryParse(isKeyDocValue, out var isKeyDoc) && isKeyDoc == 1)
                    {
                        keyDocs.Add(docItem);
                    }
                }
                catch (Exception)
                {
                    // If column doesn't exist or error reading, skip adding to key docs
                }
            }
            _logger.LogInformation("Loaded {KeyCount} key documents and {TotalCount} total documents for site {Site}", keyDocs.Count, allDocs.Count, site);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load documents from dbo.LMandConsideredSites for site {Site}.", site);
        }

        return new DocumentResult(keyDocs, allDocs);
    }

    private async Task<bool> ColumnExistsAsync(SqlConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        try
        {
            await using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @tableName 
                  AND COLUMN_NAME = @columnName";
            checkCommand.Parameters.AddWithValue("@tableName", tableName);
            checkCommand.Parameters.AddWithValue("@columnName", columnName);
            var result = await checkCommand.ExecuteScalarAsync(cancellationToken);
            return result != null && Convert.ToInt32(result) > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SiteDetails?> GetConsideredSiteDetailsAsync(string filterName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return null;
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Build query with only existing columns
            var selectColumns = new List<string>();
            var allColumns = new Dictionary<string, string>
            {
                { "Designated_x0020_Name", "Designated_x0020_Name" },
                { "Alternate_x0020_Name", "Alternate_x0020_Name" },
                { "Location", "Location" },
                { "Evaluation_x0020_Year", "Evaluation_x0020_Year" },
                { "Site_x0020_Operations", "Site_x0020_Operations" },
                { "Site_x0020_Disposition", "Site_x0020_Disposition" },
                { "Radioactive_x0020_Materials_x002", "Radioactive_x0020_Materials_x002" },
                { "Primary_x0020_Radioactive_x0020_", "Primary_x0020_Radioactive_x0020_" },
                { "Radiological_x0020_Surveys", "Radiological_x0020_Surveys" },
                { "Site_x0020_Status", "Site_x0020_Status" },
                { "Site_x0020_Summary", "Site_x0020_Summary" },
                { "LM_x0020_Site", "LM_x0020_Site" }
            };

            foreach (var column in allColumns)
            {
                if (await ColumnExistsAsync(connection, "ConsideredSiteDetails", column.Key, cancellationToken))
                {
                    selectColumns.Add($"[{column.Key}]");
                }
            }

            if (selectColumns.Count == 0)
            {
                return null;
            }

            // Get all columns from the table to build dynamic query
            await using var getAllColumnsCommand = connection.CreateCommand();
            getAllColumnsCommand.CommandText = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'ConsideredSiteDetails'
                ORDER BY ORDINAL_POSITION";
            
            var availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var columnReader = await getAllColumnsCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await columnReader.ReadAsync(cancellationToken))
                {
                    var colName = columnReader.GetString(0);
                    availableColumns.Add(colName);
                }
            }

            // Build SELECT with all available columns
            var finalSelectColumns = new List<string>();
            foreach (var column in allColumns)
            {
                if (availableColumns.Contains(column.Key))
                {
                    finalSelectColumns.Add($"[{column.Key}]");
                }
            }

            if (finalSelectColumns.Count == 0)
            {
                return null;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT TOP 1
                    {string.Join(", ", finalSelectColumns)}
                FROM ConsideredSiteDetails
                WHERE [Filter_x0020_Name] = @filterName";
            command.CommandType = CommandType.Text;
            command.Parameters.AddWithValue("@filterName", filterName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new SiteDetails
                {
                    DesignatedName = SafeGetString(reader, "Designated_x0020_Name"),
                    AlternateName = SafeGetString(reader, "Alternate_x0020_Name"),
                    Location = SafeGetString(reader, "Location"),
                    EvaluationYear = SafeGetString(reader, "Evaluation_x0020_Year"),
                    SiteOperations = SafeGetString(reader, "Site_x0020_Operations"),
                    SiteDisposition = SafeGetString(reader, "Site_x0020_Disposition"),
                    RadioactiveMaterialsHandled = SafeGetString(reader, "Radioactive_x0020_Materials_x002"),
                    PrimaryRadioactiveMaterialsHandled = SafeGetString(reader, "Primary_x0020_Radioactive_x0020_"),
                    RadiologicalSurveys = SafeGetString(reader, "Radiological_x0020_Surveys"),
                    SiteStatus = SafeGetString(reader, "Site_x0020_Status"),
                    SiteSummary = SafeGetString(reader, "Site_x0020_Summary"),
                    LmSite = SafeGetString(reader, "LM_x0020_Site")
                };
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load site details from dbo.ConsideredSiteDetails for filter {FilterName}.", filterName);
        }

        return null;
    }

    public async Task<IReadOnlyList<DocumentItem>> GetConsideredSiteDocumentsAsync(string filterName, CancellationToken cancellationToken = default)
    {
        var documents = new List<DocumentItem>();

        if (string.IsNullOrWhiteSpace(filterName))
        {
            return documents;
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get all available columns first
            await using var getAllColumnsCommand = connection.CreateCommand();
            getAllColumnsCommand.CommandText = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'ConsideredSiteDetails'
                ORDER BY ORDINAL_POSITION";
            
            var availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var columnReader = await getAllColumnsCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await columnReader.ReadAsync(cancellationToken))
                {
                    var colName = columnReader.GetString(0);
                    availableColumns.Add(colName);
                    _logger.LogInformation("ConsideredSiteDetails column found: {ColumnName}", colName);
                }
            }

            // Check which columns exist - also check alternative column names
            bool hasTitle = availableColumns.Contains("Title");
            bool hasDocDesc = availableColumns.Contains("DocDesc");
            bool hasFileLeafRef = availableColumns.Contains("FileLeafRef");
            bool hasDocumentCategory = availableColumns.Contains("Document_x0020_Category");
            bool hasState = availableColumns.Contains("State");
            bool hasSiteType = availableColumns.Contains("Site_x0020_Type");
            bool hasLMCSNames = availableColumns.Contains("LMCS_x0020_Names");
            bool hasLegacyDateCreated = availableColumns.Contains("LegacyDateCreated");
            bool hasDateCreated = availableColumns.Contains("Date_x0020_Created");
            bool hasDatePosted = availableColumns.Contains("Date_x0020_Posted");
            
            _logger.LogInformation("Columns check for documents - Title:{T} DocDesc:{D} FileLeafRef:{F} Category:{C} State:{S} SiteType:{ST} LMCSNames:{LMCS} LegacyDate:{LDate} DateCreated:{DC} DatePosted:{DP}", 
                hasTitle, hasDocDesc, hasFileLeafRef, hasDocumentCategory, hasState, hasSiteType, hasLMCSNames, hasLegacyDateCreated, hasDateCreated, hasDatePosted);

            // Build title column
            var titleParts = new List<string>();
            if (hasDocDesc) titleParts.Add("[DocDesc]");
            if (hasTitle) titleParts.Add("[Title]");
            if (hasFileLeafRef) titleParts.Add("[FileLeafRef]");
            
            // If no title columns exist, return empty list (no documents table structure)
            if (titleParts.Count == 0)
            {
                return documents;
            }
            
            // Build titleColumn: if only 1 column, don't use COALESCE
            string titleColumn;
            if (titleParts.Count == 1)
            {
                titleColumn = $"{titleParts[0]} AS Title";
            }
            else
            {
                titleColumn = $"COALESCE({string.Join(", ", titleParts)}) AS Title";
            }

            // Build other columns - use NULL for missing columns
            string categoryColumn = hasDocumentCategory 
                ? "COALESCE([Document_x0020_Category], '') AS DocumentCategory" 
                : "CAST(NULL AS NVARCHAR(MAX)) AS DocumentCategory";
            string siteTypeColumn = hasSiteType
                ? "COALESCE([Site_x0020_Type], '') AS SiteType"
                : "CAST(NULL AS NVARCHAR(MAX)) AS SiteType";
            string lmcsNamesColumn = hasLMCSNames
                ? "COALESCE([LMCS_x0020_Names], '') AS LMCSNames"
                : "CAST(NULL AS NVARCHAR(MAX)) AS LMCSNames";
            string stateColumn = hasState 
                ? "COALESCE([State], '') AS State" 
                : "CAST(NULL AS NVARCHAR(MAX)) AS State";
            
            // Use the first available date column
            string dateColumn;
            if (hasDateCreated)
            {
                dateColumn = "CASE WHEN [Date_x0020_Created] IS NOT NULL THEN CONVERT(VARCHAR(50), [Date_x0020_Created], 101) ELSE '' END AS DateCreated";
            }
            else if (hasLegacyDateCreated)
            {
                dateColumn = "CASE WHEN [LegacyDateCreated] IS NOT NULL THEN CONVERT(VARCHAR(50), [LegacyDateCreated], 101) ELSE '' END AS DateCreated";
            }
            else if (hasDatePosted)
            {
                dateColumn = "CASE WHEN [Date_x0020_Posted] IS NOT NULL THEN CONVERT(VARCHAR(50), [Date_x0020_Posted], 101) ELSE '' END AS DateCreated";
            }
            else
            {
                dateColumn = "CAST(NULL AS NVARCHAR(MAX)) AS DateCreated";
            }
            string fileRefColumn = hasFileLeafRef 
                ? "[FileLeafRef] AS FileLeafRef" 
                : "CAST(NULL AS NVARCHAR(MAX)) AS FileLeafRef";

            // Build WHERE clause - filter by Filter_x0020_Name only
            // Don't add restrictive title conditions as they might filter out valid documents
            string whereClause = "[Filter_x0020_Name] = @filterName";
            
            // Optionally add a check that at least one title field exists, but make it less restrictive
            if (titleParts.Count > 0)
            {
                // Only filter out completely NULL rows, but allow empty strings
                var nullChecks = titleParts.Select(tp => $"{tp} IS NOT NULL");
                whereClause += $" AND ({string.Join(" OR ", nullChecks)})";
            }

            // Build ORDER BY - use first available date column
            string orderByClause = "";
            if (hasDateCreated)
            {
                orderByClause = " ORDER BY [Date_x0020_Created] DESC";
            }
            else if (hasLegacyDateCreated)
            {
                orderByClause = " ORDER BY [LegacyDateCreated] DESC";
            }
            else if (hasDatePosted)
            {
                orderByClause = " ORDER BY [Date_x0020_Posted] DESC";
            }
            else if (hasTitle)
            {
                orderByClause = " ORDER BY [Title]";
            }

            await using var command = connection.CreateCommand();
            
            // Build the complete query - construct it step by step to avoid syntax errors
            var queryBuilder = new System.Text.StringBuilder();
            queryBuilder.AppendLine("SELECT");
            queryBuilder.AppendLine($"  {titleColumn},");
            queryBuilder.AppendLine($"  {categoryColumn},");
            queryBuilder.AppendLine($"  {siteTypeColumn},");
            queryBuilder.AppendLine($"  {lmcsNamesColumn},");
            queryBuilder.AppendLine($"  {stateColumn},");
            queryBuilder.AppendLine($"  {dateColumn},");
            queryBuilder.AppendLine($"  {fileRefColumn}");
            queryBuilder.AppendLine("FROM ConsideredSiteDetails");
            queryBuilder.AppendLine($"WHERE {whereClause}");
            
            if (!string.IsNullOrWhiteSpace(orderByClause))
            {
                queryBuilder.Append(orderByClause);
            }
            
            command.CommandText = queryBuilder.ToString();
            command.CommandType = CommandType.Text;
            command.Parameters.AddWithValue("@filterName", filterName);
            
            _logger.LogInformation("Executing query for filter {FilterName}: {Query}", filterName, command.CommandText);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            int rowCount = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowCount++;
                var title = SafeGetString(reader, "Title").Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    _logger.LogWarning("Skipping document row {Row} - title is empty", rowCount);
                    continue;
                }

                var filePath = hasFileLeafRef ? SafeGetString(reader, "FileLeafRef").Trim() : string.Empty;
                var category = SafeGetString(reader, "DocumentCategory").Trim();
                var state = SafeGetString(reader, "State").Trim();
                var siteType = SafeGetString(reader, "SiteType").Trim();
                var lmcsNames = SafeGetString(reader, "LMCSNames").Trim();
                var dateCreated = SafeGetString(reader, "DateCreated").Trim();

                // Use FileLeafRef as Name if available, otherwise use file icon
                var name = hasFileLeafRef && !string.IsNullOrWhiteSpace(filePath) ? filePath : "📄";

                documents.Add(new DocumentItem
                {
                    Name = name,
                    Title = title,
                    Category = category,
                    SiteType = siteType,
                    LMCSNames = lmcsNames,
                    State = state,
                    DateCreated = dateCreated,
                    FilePath = filePath
                });
                
                _logger.LogDebug("Added document: Title={Title}, Category={Category}, SiteType={SiteType}, LMCSNames={LMCSNames}, State={State}, Date={Date}", 
                    title, category, siteType, lmcsNames, state, dateCreated);
            }
            
            _logger.LogInformation("Loaded {Count} documents for filter {FilterName}", documents.Count, filterName);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load documents from dbo.ConsideredSiteDetails for filter {FilterName}. Error: {Error}", 
                filterName, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading documents for filter {FilterName}. Error: {Error}", 
                filterName, ex.Message);
        }

        return documents;
    }
}

public class DocumentItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
    
    [JsonPropertyName("siteType")]
    public string SiteType { get; set; } = string.Empty;
    
    [JsonPropertyName("lmcsNames")]
    public string LMCSNames { get; set; } = string.Empty;
    
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    
    [JsonPropertyName("dateCreated")]
    public string DateCreated { get; set; } = string.Empty;
    
    [JsonPropertyName("datePosted")]
    public string DatePosted { get; set; } = string.Empty;
    
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;
}

public class DocumentResult
{
    public DocumentResult(IReadOnlyList<DocumentItem> keyDocuments, IReadOnlyList<DocumentItem> siteDocuments)
    {
        KeyDocuments = keyDocuments;
        SiteDocuments = siteDocuments;
    }

    [JsonPropertyName("keyDocuments")]
    public IReadOnlyList<DocumentItem> KeyDocuments { get; }
    
    [JsonPropertyName("siteDocuments")]
    public IReadOnlyList<DocumentItem> SiteDocuments { get; }
}

public class SiteDetails
{
    [JsonPropertyName("designatedName")]
    public string DesignatedName { get; set; } = string.Empty;
    
    [JsonPropertyName("alternateName")]
    public string AlternateName { get; set; } = string.Empty;
    
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
    
    [JsonPropertyName("evaluationYear")]
    public string EvaluationYear { get; set; } = string.Empty;
    
    [JsonPropertyName("siteOperations")]
    public string SiteOperations { get; set; } = string.Empty;
    
    [JsonPropertyName("siteDisposition")]
    public string SiteDisposition { get; set; } = string.Empty;
    
    [JsonPropertyName("radioactiveMaterialsHandled")]
    public string RadioactiveMaterialsHandled { get; set; } = string.Empty;
    
    [JsonPropertyName("primaryRadioactiveMaterialsHandled")]
    public string PrimaryRadioactiveMaterialsHandled { get; set; } = string.Empty;
    
    [JsonPropertyName("radiologicalSurveys")]
    public string RadiologicalSurveys { get; set; } = string.Empty;
    
    [JsonPropertyName("siteStatus")]
    public string SiteStatus { get; set; } = string.Empty;
    
    [JsonPropertyName("siteSummary")]
    public string SiteSummary { get; set; } = string.Empty;
    
    [JsonPropertyName("lmSite")]
    public string LmSite { get; set; } = string.Empty;
}

internal static class StringExtensions
{
    public static string IfEmpty(this string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}


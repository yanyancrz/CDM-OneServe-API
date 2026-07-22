using CDM_OneServe_API.DTOs;
using MySqlConnector;

namespace CDM_OneServe_API.Services
{
    public class LibraryActivityService
    {
        private readonly IConfiguration _configuration;

        public LibraryActivityService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")!;

        public async Task<List<LibraryActivityDto>> GetActivitiesAsync(int userId)
        {
            var activities = new List<LibraryActivityDto>();

            using var connection = new MySqlConnection(ConnectionString);

            await connection.OpenAsync();

            const string sql = @"
                SELECT
                    la.ActivityId,
                    la.BookId,
                    la.ActivityType,
                    la.Title,
                    la.Description,
                    la.CreatedAt,
                    b.CoverImage
                FROM LibraryActivities la
                LEFT JOIN Books b
                    ON la.BookId = b.BookId
                WHERE la.UserId = @UserId
                ORDER BY la.CreatedAt DESC;
            ";

            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@UserId", userId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                activities.Add(new LibraryActivityDto
                {
                    ActivityId = Convert.ToInt32(reader["ActivityId"]),

                    BookId = reader["BookId"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(reader["BookId"]),

                    Type = reader["ActivityType"]?.ToString() ?? "",

                    Title = reader["Title"]?.ToString() ?? "",

                    Description = reader["Description"] == DBNull.Value
                        ? ""
                        : reader["Description"].ToString()!,

                    Date = reader.GetDateTime("CreatedAt"),
                    

                    CoverImage = reader["CoverImage"] == DBNull.Value
                        ? null
                        : reader["CoverImage"].ToString()
                });
            }

            return activities;
        }
    }
}
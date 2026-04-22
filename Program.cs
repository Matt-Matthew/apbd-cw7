using Microsoft.Data.SqlClient;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

var connString = builder.Configuration.GetConnectionString("DefaultConnection");
try
{
    using var connection = new SqlConnection(connString);
    connection.Open();
    var sql = "SELECT IdPatient, FirstName, LastName, Email FROM dbo.Patients";
    using var command = new SqlCommand(sql, connection);
    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        var id = reader["IdPatient"];
        var firstName = reader["FirstName"];
        var lastName = reader["LastName"];
        var email = reader["Email"];

        Console.WriteLine($"[{id}] {firstName} {lastName} ({email})");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n=== BŁĄD POŁĄCZENIA: {ex.Message} ===\n");
}


app.Run();

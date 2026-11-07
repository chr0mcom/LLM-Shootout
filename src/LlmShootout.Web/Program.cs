using LlmShootout.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddShootoutConfiguration();
builder.Services.AddShootout(builder.Configuration);

var app = builder.Build();
app.UseShootout();
app.Run();

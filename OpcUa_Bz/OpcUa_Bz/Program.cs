using OpcUa_Bz.Client;
using OpcUa_Bz.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Register the required services
builder.Services.AddSingleton<OpcuaManagement>();  
builder.Services.AddSingleton<OpcUaServerService>();

// Add antiforgery services
builder.Services.AddAntiforgery();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Add UseAntiforgery middleware here, after UseRouting and before UseEndpoints
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(OpcUa_Bz.Client._Imports).Assembly);

app.Run();

using sapaXPrintService;

var builder = Host.CreateApplicationBuilder(args);

// Lets the program run as a Windows service
builder.Services.AddWindowsService(options => options.ServiceName = "sapaXPrintService");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
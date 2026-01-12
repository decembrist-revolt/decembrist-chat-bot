FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR '/src'
COPY 'DecembristChatBot.sln' '.'
COPY 'DecembristChatBotSharp/DecembristChatBotSharp.csproj' 'DecembristChatBotSharp/'
RUN dotnet restore 'DecembristChatBotSharp/DecembristChatBotSharp.csproj'
COPY . .
RUN dotnet publish 'DecembristChatBotSharp/DecembristChatBotSharp.csproj' -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# Install SkiaSharp dependencies
RUN apt-get update && apt-get install -y \
    libfontconfig1 \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR '/app'
COPY --from=build '/app/publish' .
EXPOSE 80
CMD ["dotnet", "DecembristChatBotSharp.dll"]
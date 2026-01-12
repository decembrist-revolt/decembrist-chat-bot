FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR '/src'
COPY 'DecembristChatBot.sln' '.'
COPY 'DecembristChatBotSharp/DecembristChatBotSharp.csproj' 'DecembristChatBotSharp/'
RUN dotnet restore 'DecembristChatBotSharp/DecembristChatBotSharp.csproj'
COPY . .
RUN dotnet publish 'DecembristChatBotSharp/DecembristChatBotSharp.csproj' -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR '/app'
COPY --from=build '/app/publish' .
EXPOSE 80
CMD ["dotnet", "DecembristChatBotSharp.dll"]
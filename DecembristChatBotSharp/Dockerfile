FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DecembristChatBotSharp/DecembristChatBotSharp.csproj", "DecembristChatBotSharp/"]
RUN dotnet restore "DecembristChatBotSharp/DecembristChatBotSharp.csproj"
COPY . .
WORKDIR "/src/DecembristChatBotSharp"
RUN dotnet build "DecembristChatBotSharp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DecembristChatBotSharp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DecembristChatBotSharp.dll"]

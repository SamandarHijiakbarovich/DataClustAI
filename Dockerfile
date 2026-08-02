# ---- Build bosqichi ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Avval faqat csproj — restore keshini samarali ishlatish uchun
COPY ExcelAiCategorizer.csproj ./
RUN dotnet restore ExcelAiCategorizer.csproj

# Qolgan kod
COPY . ./
RUN dotnet publish ExcelAiCategorizer.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime bosqichi ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

# Render PORT muhit o'zgaruvchisini beradi (odatda 10000).
# Program.cs uni o'qib, http://0.0.0.0:$PORT ni tinglaydi.
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ExcelAiCategorizer.dll"]

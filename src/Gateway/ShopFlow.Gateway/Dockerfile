# Tệp này build image cho Gateway YARP theo multi-stage, chỉ cần compile và publish project gateway.
# Chúng ta không nhúng database hay credential vào image vì gateway chỉ proxy request và không sở hữu data store.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sao chép project gateway trước để restore tối thiểu và giảm thời gian build khi code thay đổi.
COPY src/Gateway/ShopFlow.Gateway/ShopFlow.Gateway.csproj src/Gateway/ShopFlow.Gateway/
RUN dotnet restore src/Gateway/ShopFlow.Gateway/ShopFlow.Gateway.csproj

# Copy toàn bộ source trong repo để publish project sau khi restore xong.
COPY . .
RUN dotnet publish src/Gateway/ShopFlow.Gateway/ShopFlow.Gateway.csproj -c Release -o /app/publish --no-restore

# Stage cuối dùng image ASP.NET runtime bare để chạy gateway đã build ở stage trước.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Container chạy gateway trên cổng 5000 do biến môi trường ASPNETCORE_URLS cấu hình trong compose.
ENTRYPOINT ["dotnet", "ShopFlow.Gateway.dll"]

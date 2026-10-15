# Build context: the repository root, so the image gets the default screens.json.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY router/FrameRouter.slnx router/
COPY router/src/FrameRouter/FrameRouter.csproj router/src/FrameRouter/
COPY router/tests/FrameRouter.Tests/FrameRouter.Tests.csproj router/tests/FrameRouter.Tests/
# The test project references the app: one restore covers both.
RUN dotnet restore router/tests/FrameRouter.Tests
COPY router/ router/
COPY screens.json ./
# A red test fails the image build.
RUN dotnet test router/tests/FrameRouter.Tests -c Release --no-restore
# After the tests: a new commit does not run them again when the code is the same. Empty in a local build.
ARG GIT_SHA=
RUN dotnet publish router/src/FrameRouter -c Release --no-restore -o /app/publish /p:UseAppHost=false /p:SourceRevisionId=$GIT_SHA

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV FrameRouter__DataDirectory=/app/data
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FrameRouter.dll"]

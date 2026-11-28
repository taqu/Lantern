  1. Download meilisearch.exe to tools/meilisearch/, set MEILI_MASTER_KEY
  2. .\scripts\start-meilisearch.ps1
  3. dotnet run --project tools/Lantern.Feasibility -- --seed
  4. dotnet run --project src/Lantern.Web
  5. Open http://localhost:5000/search

cd "y:\WildwoodAPI\Dev\WildwoodComponents"
dotnet build > build_results.txt 2>&1
echo Build completed
type build_results.txt

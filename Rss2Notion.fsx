#r "nuget:System.ServiceModel.Syndication"
#r "nuget:Notion.Net"

open System
open System.Xml
open System.Collections.Generic

open System.ServiceModel.Syndication
open Notion.Client

let getWeeklyFeedItems (now: DateTime) (url: string) =
    let reader = XmlReader.Create(url)
    let feed = SyndicationFeed.Load(reader)
    feed.Items 
        |> Seq.filter (fun x -> now - x.LastUpdatedTime.DateTime <= TimeSpan.FromDays(7.))
        |> Seq.map (fun x -> x.Title.Text, string (x.Links |> Seq.head).Uri)

let createPageIfNotExists (client: NotionClient) (databaseId: string) ((title, link): (string * string)) =
    async {
        let page = PagesCreateParametersBuilder.Create(DatabaseParentInput(DatabaseId = databaseId))
                        .AddProperty("Title", TitlePropertyValue(Title=List<RichTextBase>([ RichTextText(Text=new Text(Content=title)) :> RichTextBase ] |> Seq.ofList)))
                        .AddProperty("Link", UrlPropertyValue(Url= link))
                        .Build()

        try
            let queryParameters = DatabasesQueryParameters(Filter = TextFilter("Link", equal=link))
            let! searchPageResult = client.Databases.QueryAsync(databaseId, queryParameters) |> Async.AwaitTask

            match searchPageResult.Results.Count = 0 with
            | true -> 
                let! _ = client.Pages.CreateAsync(page) |> Async.AwaitTask
                printfn $"[CREATED] - {title} ({link})"
            | false -> 
                printfn $"[EXISTS]  - {title} ({link})"
        with
        | _ -> printfn $"[ERROR]   - {title} ({link})"
    }

let authToken = Environment.GetEnvironmentVariable("NOTION_API_TOKEN")
let databaseId = Environment.GetEnvironmentVariable("NOTION_FEEDITEMS_DATABASE_ID")

let client = NotionClient(ClientOptions(AuthToken = authToken))

[
    "https://brandewinder.com/atom.xml"
    "https://codeopinion.com/feed/atom/"
    "https://blog.ploeh.dk/atom"
]
    |> Seq.collect (getWeeklyFeedItems DateTime.Now)
    |> Seq.map (createPageIfNotExists client databaseId)
    |> Async.Parallel
    |> Async.Ignore
    |> Async.RunSynchronously

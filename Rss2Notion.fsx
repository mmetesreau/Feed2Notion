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
        |> Seq.map (fun x -> x.Title.Text,  x.Links[0].Uri |> string)

let createPageIfNotExists (client: NotionClient) (databaseId: string) ((title, link): (string * string)) =
    async {
        let page = PagesCreateParametersBuilder.Create(DatabaseParentInput(DatabaseId = databaseId))
                        .AddProperty("Title", TitlePropertyValue(Title=List<RichTextBase>([ RichTextText(Text=new Text(Content=title)) :> RichTextBase ] |> Seq.ofList)))
                        .AddProperty("Link", UrlPropertyValue(Url= link))
                        .Build()

        try
            let! searchPageResult = client.Databases.QueryAsync(databaseId, DatabasesQueryParameters(Filter = TextFilter("Link", equal=link))) |> Async.AwaitTask

            if searchPageResult.Results.Count = 0 then
                let! _ = client.Pages.CreateAsync(page) |> Async.AwaitTask
                return $"[CREATED] - {title} ({link})"
            else 
                return $"[EXISTS] - {title} ({link})"
        with
        | e -> return $"[ERROR] - {title} ({link}) {e.Message}"
    }

let authToken = Environment.GetEnvironmentVariable("NOTION_API_TOKEN")
let databaseId = Environment.GetEnvironmentVariable("NOTION_FEEDITEMS_DATABASE_ID")

let client = NotionClientFactory.Create(ClientOptions(AuthToken = authToken))

[
    "https://brandewinder.com/atom.xml"
    "https://sergeytihon.com/feed/atom/"
    "https://codeopinion.com/feed/atom/"
    "https://blog.tunaxor.me/feed.atom"
    "https://thinkbeforecoding.com/feed/atom"
    "https://blog.ploeh.dk/atom"
]
    |> Seq.collect (getWeeklyFeedItems DateTime.Now)
    |> Seq.map (createPageIfNotExists client databaseId)
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Seq.iter (fun x -> printfn $"{x}")

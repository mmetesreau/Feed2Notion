#r "nuget: FSharp.Data"
#r "nuget: FSharp.Control.AsyncSeq"
#r "nuget: Notion.Net"

open System
open System.Collections.Generic

open FSharp.Control
open FSharp.Data
open Notion.Client

[<Literal>]
let atomSchema = """
"""

[<Literal>]
let feedExample = """<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom">
 <entry>
   <title>Title</title>
   <link rel="alternate" href="http://example.org/2003/12/13/atom03"/>
   <link rel="replies" href="http://example.org/2003/12/13/atom03"/>
   <updated>2021-12-17T08:55:54.2019611+00:00</updated>
 </entry>
 <entry>
   <title>Title</title>
   <link rel="alternate" href="http://example.org/2003/12/13/atom03"/>
   <link rel="replies" href="http://example.org/2003/12/13/atom03"/>
   <updated>2021-12-17T08:55:54.2019611+00:00</updated>
 </entry>
</feed>
"""
  
type FeedProvider = XmlProvider<feedExample>

let getWeeklyFeedItems (now: DateTime) (urls: string list) = asyncSeq {
    for url in urls do
        let! result = FeedProvider.AsyncLoad(url)
        for entry in result.Entries do
            if now - entry.Updated.DateTime <= TimeSpan.FromDays(15.) then
                yield entry.Title, entry.Links |> Seq.head |> fun x -> x.Href
}

let createPageIfNotExists (client: NotionClient) (databaseId: string) ((title, link): (string * string)) = async {
    let page = PagesCreateParametersBuilder.Create(DatabaseParentInput(DatabaseId = databaseId))
                    .AddProperty("Title", TitlePropertyValue(Title=List<RichTextBase>([ RichTextText(Text=new Text(Content=title)) :> RichTextBase ] |> Seq.ofList)))
                    .AddProperty("Link", UrlPropertyValue(Url=link))
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
    "https://thinkbeforecoding.com/feed/atom"
    "https://brandewinder.com/atom.xml"
    "https://sergeytihon.com/feed/atom/"
    "https://codeopinion.com/feed/atom/"
    "https://blog.tunaxor.me/feed.atom"
    "https://blog.ploeh.dk/atom"
]
    |> getWeeklyFeedItems DateTime.Now 
    |> AsyncSeq.mapAsync (createPageIfNotExists client databaseId)
    |> AsyncSeq.iter (printfn "%s")
    |> Async.RunSynchronously

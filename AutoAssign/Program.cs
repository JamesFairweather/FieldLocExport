namespace AutoAssign
{
    // To get game requests, we can use an undocumented API call:
    // https://littlemountainbaseball.assignr.com/assign/games/{gameId}/edit
    // This returns a small form in HTML format, but it's easily parsed.  Goto form->table->Pending Requests->find the tag data-game-id="{gameId}" data-assignment-id="{out_requestId_Plate}" e.g. 77199786
    // Then, we can do a get on this UTRL: https://littlemountainbaseball.assignr.com/assign/assignments/77199786.json
    // returns a JSON object with all users, each one indicating whether they've submitted a request for that game.  All the people who requested the game are listed at the top.
    // There isn't a public API to assign an official, but there is one to unassign all officials: /v2/games/{id}/unassign
    // To assign an official: 
    // PUT https://littlemountainbaseball.assignr.com/assign/games/{gameId}
    // Have to pass a User ID (mine is 340011)
    // As well as the assignment Id (e.g. 77267314)
    // It's not clear to me how these parameters are being passed back to the service from the stream I can see in Chrome

    internal partial class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            return 0;
        }
    }
}

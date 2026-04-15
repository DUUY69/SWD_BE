using System; using System.Net.Http; using System.Threading.Tasks;
class Program {
    static async Task Main() {
        var client = new HttpClient();
        for (int i = 8; i <= 15; i++) {
            var res = await client.GetAsync($"http://localhost:5064/api/exams/{i}/questions");
            if(res.IsSuccessStatusCode) {
                var json = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"Exam {i}: {json.Substring(0, Math.Min(200, json.Length))}...");
            }
        }
    }
}

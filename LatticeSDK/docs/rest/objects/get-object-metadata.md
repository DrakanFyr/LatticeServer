# Get object metadata

HEAD https://example.developer.anduril.com/api/v1/objects/{objectPath}

Returns metadata for a specified object path. Use this to fetch metadata such as object size (size_bytes), its expiry time (expiry_time), or its latest update timestamp (last_updated_at).

Reference: https://developer.anduril.com/reference/rest/objects/get-object-metadata

## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/objects/{objectPath}:
    head:
      operationId: get-object-metadata
      summary: Get object metadata
      description: >-
        Returns metadata for a specified object path. Use this to fetch metadata
        such as object size (size_bytes), its expiry time (expiry_time), or its
        latest update timestamp (last_updated_at).
      tags:
        - subpackage_objects
      parameters:
        - name: objectPath
          in: path
          description: The path of the object to query.
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Successful operation
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Objects_getObjectMetadata_Response_200'
        '400':
          description: Not Found
          content:
            application/json:
              schema:
                description: Any type
        '401':
          description: Unauthorized
          content:
            application/json:
              schema:
                description: Any type
        '500':
          description: Internal Server Error
          content:
            application/json:
              schema:
                description: Any type
servers:
  - url: https://example.developer.anduril.com
components:
  schemas:
    Objects_getObjectMetadata_Response_200:
      type: object
      properties: {}
      description: Empty response body
      title: Objects_getObjectMetadata_Response_200
  securitySchemes:
    OAuth:
      type: http
      scheme: bearer

```

## SDK Code Examples

```python
import requests

url = "https://example.developer.anduril.com/api/v1/objects/objectPath"

headers = {"Authorization": "Bearer <token>"}

response = requests.head(url, headers=headers)

print(response.json())
```

```javascript
const url = 'https://example.developer.anduril.com/api/v1/objects/objectPath';
const options = {method: 'HEAD', headers: {Authorization: 'Bearer <token>'}};

try {
  const response = await fetch(url, options);
  const data = await response.json();
  console.log(data);
} catch (error) {
  console.error(error);
}
```

```go
package main

import (
	"fmt"
	"net/http"
	"io"
)

func main() {

	url := "https://example.developer.anduril.com/api/v1/objects/objectPath"

	req, _ := http.NewRequest("HEAD", url, nil)

	req.Header.Add("Authorization", "Bearer <token>")

	res, _ := http.DefaultClient.Do(req)

	defer res.Body.Close()
	body, _ := io.ReadAll(res.Body)

	fmt.Println(res)
	fmt.Println(string(body))

}
```

```ruby
require 'uri'
require 'net/http'

url = URI("https://example.developer.anduril.com/api/v1/objects/objectPath")

http = Net::HTTP.new(url.host, url.port)
http.use_ssl = true

request = Net::HTTP::Head.new(url)
request["Authorization"] = 'Bearer <token>'

response = http.request(request)
puts response.read_body
```

```java
import com.mashape.unirest.http.HttpResponse;
import com.mashape.unirest.http.Unirest;

HttpResponse<String> response = Unirest.head("https://example.developer.anduril.com/api/v1/objects/objectPath")
  .header("Authorization", "Bearer <token>")
  .asString();
```

```php
<?php
require_once('vendor/autoload.php');

$client = new \GuzzleHttp\Client();

$response = $client->request('HEAD', 'https://example.developer.anduril.com/api/v1/objects/objectPath', [
  'headers' => [
    'Authorization' => 'Bearer <token>',
  ],
]);

echo $response->getBody();
```

```csharp
using RestSharp;

var client = new RestClient("https://example.developer.anduril.com/api/v1/objects/objectPath");
var request = new RestRequest(Method.HEAD);
request.AddHeader("Authorization", "Bearer <token>");
IRestResponse response = client.Execute(request);
```

```swift
import Foundation

let headers = ["Authorization": "Bearer <token>"]

let request = NSMutableURLRequest(url: NSURL(string: "https://example.developer.anduril.com/api/v1/objects/objectPath")! as URL,
                                        cachePolicy: .useProtocolCachePolicy,
                                    timeoutInterval: 10.0)
request.httpMethod = "HEAD"
request.allHTTPHeaderFields = headers

let session = URLSession.shared
let dataTask = session.dataTask(with: request as URLRequest, completionHandler: { (data, response, error) -> Void in
  if (error != nil) {
    print(error as Any)
  } else {
    let httpResponse = response as? HTTPURLResponse
    print(httpResponse)
  }
})

dataTask.resume()
```
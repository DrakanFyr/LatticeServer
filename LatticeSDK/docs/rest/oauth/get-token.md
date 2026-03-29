# Get OAuth2 token

POST https://example.developer.anduril.com/api/v1/oauth/token
Content-Type: application/x-www-form-urlencoded

Gets a new short-lived token using the specified client credentials

Reference: https://developer.anduril.com/reference/rest/oauth/get-token

## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/oauth/token:
    post:
      operationId: get-token
      summary: Get OAuth2 token
      description: Gets a new short-lived token using the specified client credentials
      tags:
        - subpackage_oauth
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Access token response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/oauth_getToken_Response_200'
        '400':
          description: Bad request or invalid request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetTokenRequestBadRequestError'
        '401':
          description: Unauthorized - client authentication failed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetTokenRequestUnauthorizedError'
      requestBody:
        content:
          application/json:
            schema:
              type: object
              properties:
                grant_type:
                  type: string
                  enum:
                    - client_credentials
                  description: The type of grant being requested
                client_id:
                  type: string
                  description: The client identifier
                client_secret:
                  type: string
                  format: password
                  description: The client secret
              required:
                - grant_type
servers:
  - url: https://example.developer.anduril.com
components:
  schemas:
    oauth_getToken_Response_200:
      type: object
      properties:
        access_token:
          type: string
          description: The access token
        token_type:
          type: string
          description: The type of token (typically "Bearer")
        expires_in:
          type: integer
          description: Lifetime of the access token in seconds
        refresh_expires_in:
          type: integer
          description: Lifetime of the refresh token
        not-before-policy:
          type: integer
          description: Enforce that a token cannot be used before a specific unixtime
        scope:
          type: string
          description: The scope of the access token
      required:
        - access_token
        - token_type
      title: oauth_getToken_Response_200
    GetTokenRequestBadRequestError:
      type: object
      properties:
        error:
          type: string
        error_description:
          type: string
      required:
        - error
      title: GetTokenRequestBadRequestError
    GetTokenRequestUnauthorizedError:
      type: object
      properties:
        error:
          type: string
        error_description:
          type: string
      required:
        - error
      title: GetTokenRequestUnauthorizedError
  securitySchemes:
    OAuth:
      type: http
      scheme: bearer

```

## SDK Code Examples

```python
import requests

url = "https://example.developer.anduril.com/api/v1/oauth/token"

payload = ""
headers = {
    "Authorization": "Bearer <token>",
    "Content-Type": "application/x-www-form-urlencoded"
}

response = requests.post(url, data=payload, headers=headers)

print(response.json())
```

```javascript
const url = 'https://example.developer.anduril.com/api/v1/oauth/token';
const options = {
  method: 'POST',
  headers: {
    Authorization: 'Bearer <token>',
    'Content-Type': 'application/x-www-form-urlencoded'
  },
  body: new URLSearchParams('')
};

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

	url := "https://example.developer.anduril.com/api/v1/oauth/token"

	req, _ := http.NewRequest("POST", url, nil)

	req.Header.Add("Authorization", "Bearer <token>")
	req.Header.Add("Content-Type", "application/x-www-form-urlencoded")

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

url = URI("https://example.developer.anduril.com/api/v1/oauth/token")

http = Net::HTTP.new(url.host, url.port)
http.use_ssl = true

request = Net::HTTP::Post.new(url)
request["Authorization"] = 'Bearer <token>'
request["Content-Type"] = 'application/x-www-form-urlencoded'

response = http.request(request)
puts response.read_body
```

```java
import com.mashape.unirest.http.HttpResponse;
import com.mashape.unirest.http.Unirest;

HttpResponse<String> response = Unirest.post("https://example.developer.anduril.com/api/v1/oauth/token")
  .header("Authorization", "Bearer <token>")
  .header("Content-Type", "application/x-www-form-urlencoded")
  .asString();
```

```php
<?php
require_once('vendor/autoload.php');

$client = new \GuzzleHttp\Client();

$response = $client->request('POST', 'https://example.developer.anduril.com/api/v1/oauth/token', [
  'form_params' => null,
  'headers' => [
    'Authorization' => 'Bearer <token>',
    'Content-Type' => 'application/x-www-form-urlencoded',
  ],
]);

echo $response->getBody();
```

```csharp
using RestSharp;

var client = new RestClient("https://example.developer.anduril.com/api/v1/oauth/token");
var request = new RestRequest(Method.POST);
request.AddHeader("Authorization", "Bearer <token>");
request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
IRestResponse response = client.Execute(request);
```

```swift
import Foundation

let headers = [
  "Authorization": "Bearer <token>",
  "Content-Type": "application/x-www-form-urlencoded"
]

let request = NSMutableURLRequest(url: NSURL(string: "https://example.developer.anduril.com/api/v1/oauth/token")! as URL,
                                        cachePolicy: .useProtocolCachePolicy,
                                    timeoutInterval: 10.0)
request.httpMethod = "POST"
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
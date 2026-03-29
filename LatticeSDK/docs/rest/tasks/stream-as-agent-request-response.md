## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/agent/stream:
    post:
      operationId: stream-as-agent
      summary: Stream as agent
      description: >-
        Establishes a server streaming connection that delivers tasks to
        taskable agents for execution

        using Server-Sent Events (SSE).


        This method creates a connection from the Tasks API to an agent that
        streams relevant tasks to the listener agent. The agent receives a
        stream of tasks that match the entities specified by the tasks' selector
        criteria.


        The stream delivers three types of requests:

        - `ExecuteRequest`: Contains a new task for the agent to execute

        - `CancelRequest`: Indicates a task should be canceled

        - `CompleteRequest`: Indicates a task should be completed


        Additionally, heartbeat messages are sent periodically to maintain the
        connection.


        This is recommended method for taskable agents to receive and process
        tasks in real-time.

        Agents should maintain connection to this stream and process incoming
        tasks according to their capabilities. 


        When an agent receives a task, it should update the task status using
        the `UpdateStatus` endpoint

        to provide progress information back to Tasks API.
      tags:
        - subpackage_tasks
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Returns a stream of tasks to the agent as they become available.
          content:
            text/event-stream:
              schema:
                $ref: '#/components/schemas/Tasks_streamAsAgent_Response_200'
        '400':
          description: Bad request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '401':
          description: Unauthorized to access resource
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AgentStreamRequest'
servers:
  - url: https://example.developer.anduril.com

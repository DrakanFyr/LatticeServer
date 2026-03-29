## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/agent/listen:
    post:
      operationId: listen-as-agent
      summary: Listen as agent
      description: >-
        Establishes a server streaming connection that delivers tasks to
        taskable agents for execution.


        This method creates a persistent connection from Tasks API to an agent,
        allowing the server

        to push tasks to the agent as they become available. The agent receives
        a stream of tasks that

        match its selector criteria (entity IDs).


        The stream delivers three types of requests:

        - ExecuteRequest: Contains a new task for the agent to execute

        - CancelRequest: Indicates a task should be canceled

        - CompleteRequest: Indicates a task should be completed


        This is the primary method for taskable agents to receive and process
        tasks in real-time.

        Agents should maintain this connection and process incoming tasks
        according to their capabilities.


        When an agent receives a task, it should update the task status using
        the UpdateStatus endpoint

        to provide progress information back to Tasks API.


        This is a long polling API that will block until a new task is ready for
        delivery. If no new task is

        available then the server will hold on to your request for up to 5
        minutes, after that 5 minute timeout

        period you will be expected to reinitiate a new request.
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
          description: Requests for the agent to comply with.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AgentRequest'
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
              $ref: '#/components/schemas/AgentListener'
servers:
  - url: https://example.developer.anduril.com
